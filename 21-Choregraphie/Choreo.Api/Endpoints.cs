using Choreo.Bus;
using Choreo.Services;
using Choreo.Services.Contrats;

namespace Choreo.Api;

internal static class Endpoints
{
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);
    internal record DecisionRequete(bool Approuve);
    internal record ChaosRequete(bool? ChangeEnPanne, int? PannesNotifications);

    public static void MapVirements(this IEndpointRouteBuilder app)
    {
        // Demander un virement, c'est publier UN événement. La réponse est 202 : la suite se fera sans nous.
        // ?attendre=true attend la fin (d'après le Suivi), pour la commodité de la démo.
        app.MapPost("/virements", async (VirementRequete r, bool? attendre, Banque banque) =>
        {
            var id = banque.Demander(r.Source, r.Cible, r.Montant);
            if (attendre != true)
                return Results.Accepted($"/virements/{id}", new { id });
            return Results.Ok(await banque.Attendre(id));
        });

        app.MapGet("/virements", (Banque banque) => Results.Ok(banque.Suivi.Tous()));

        app.MapGet("/virements/bloques", (int? delaiSecondes, Banque banque) =>
            Results.Ok(banque.Suivi.Bloques(banque.Horloge.GetUtcNow(), TimeSpan.FromSeconds(delaiSecondes ?? 10))));

        // L'état d'après le Suivi, et l'histoire complète : chaque événement, qui l'a publié, et en réaction à quoi.
        app.MapGet("/virements/{id}", (string id, Banque banque) =>
        {
            var evenements = banque.Bus.Journal(id);
            if (evenements.Count == 0)
                return Results.NotFound();
            return Results.Ok(new { etat = banque.Suivi.Etat(id), historique = Historique(evenements) });
        });
    }

    public static void MapServices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/comptes/{id:guid}", (Guid id, Banque banque) =>
            banque.Comptes.Consulter(id) is { } compte ? Results.Ok(compte) : Results.NotFound());

        app.MapGet("/conformite/en-attente", (Banque banque) => Results.Ok(banque.Conformite.EnAttente()));

        // Un événement venu d'un humain : la décision de l'analyste.
        app.MapPost("/conformite/{id}/decision", (string id, DecisionRequete r, Banque banque) =>
            banque.Conformite.Decider(id, r.Approuve) ? Results.NoContent()
                : Results.NotFound(new { erreur = "Aucune décision attendue pour ce virement (inconnu, déjà décidé, ou abandonné)." }));

        app.MapGet("/notifications", (Banque banque) => Results.Ok(banque.Notifications.Envoyees()));

        app.MapGet("/fidelite/{id:guid}", (Guid id, Banque banque) =>
            banque.Fidelite is { } fidelite ? Results.Ok(new { compte = id, points = fidelite.Points(id) }) : Results.NotFound());

        app.MapPost("/chaos", (ChaosRequete r, Banque banque) =>
        {
            if (r.ChangeEnPanne is { } panne)
                banque.Change.EnPanne = panne;
            if (r.PannesNotifications is { } pannes)
                banque.Notifications.Saboter(pannes);
            return Results.Ok(new { changeEnPanne = banque.Change.EnPanne });
        });
    }

    public static void MapBus(this IEndpointRouteBuilder app)
    {
        var bus = app.MapGroup("/bus");

        // Qui écoute quoi : la seule « carte » du processus, et elle n'existe qu'à l'exécution.
        bus.MapGet("/", (Banque banque) => Results.Ok(banque.Bus.Abonnes()));

        bus.MapGet("/lettres-mortes", (Banque banque) => Results.Ok(banque.Bus.LettresMortes().Select(l => new
        {
            l.Abonne, l.Tentatives, l.Erreur, evenement = l.Evenement.Type, virement = l.Evenement.Correlation
        })));

        bus.MapGet("/journal", (int? dernier, Banque banque) =>
            Results.Ok(banque.Bus.Journal().TakeLast(dernier ?? 50).Select(e => new { e.Date, e.Type, e.Emetteur, virement = e.Correlation })));
    }

    /// <summary>Les événements d'un virement, chacun indenté sous celui qui l'a provoqué : la chaîne des réactions.</summary>
    private static IEnumerable<object> Historique(IReadOnlyList<Evenement> evenements)
    {
        var profondeurs = new Dictionary<Guid, int>();
        var types = evenements.ToDictionary(e => e.Id, e => e.Type);
        foreach (var e in evenements)
        {
            var profondeur = e.Cause is { } cause && profondeurs.TryGetValue(cause, out var p) ? p + 1 : 0;
            profondeurs[e.Id] = profondeur;
            yield return new
            {
                evenement = $"{new string(' ', 2 * profondeur)}{e.Type}",
                e.Emetteur,
                enReactionA = e.Cause is { } c ? types.GetValueOrDefault(c) : null,
                e.Date,
                motif = e switch
                {
                    FondsRefuses f => f.Motif, ConformiteRefusee f => f.Motif, ChangeEchoue f => f.Motif,
                    FondsLiberes f => f.Motif, VirementAbandonne f => f.Motif, ClientNotifie f => f.Message, _ => null
                }
            };
        }
    }
}
