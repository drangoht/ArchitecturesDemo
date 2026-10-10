using System.Text.Json;
using Es.Domaine;
using Es.Projections;
using Es.Stockage;
using Microsoft.AspNetCore.Mvc;

namespace Es.Api;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

internal static class Endpoints
{
    internal record OuvertureRequete(string Titulaire, decimal DepotInitial);
    internal record MontantRequete(decimal Montant);
    internal record RetraitRequete(decimal Montant, string? Motif);
    internal record VirementRequete(Guid Source, Guid Cible, decimal Montant);

    // ─── Commandes : charger (rejouer) → décider → ajouter les nouveaux événements ───

    public static void MapCommandes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/comptes", async (OuvertureRequete r, DepotComptes depot, TimeProvider horloge) =>
        {
            var compte = Compte.Ouvrir(Guid.NewGuid(), r.Titulaire, r.DepotInitial, horloge.GetUtcNow().UtcDateTime);
            await depot.Enregistrer(compte);
            return Results.Created($"/comptes/{compte.Id}", new { compte.Id, compte.Version });
        });

        app.MapPost("/comptes/{id:guid}/depot", (Guid id, MontantRequete r, [FromHeader(Name = "If-Match")] int? version,
                DepotComptes depot, TimeProvider horloge) =>
            Modifier(id, version, depot, c => c.Deposer(r.Montant, horloge.GetUtcNow().UtcDateTime)));

        app.MapPost("/comptes/{id:guid}/retrait", (Guid id, RetraitRequete r, [FromHeader(Name = "If-Match")] int? version,
                DepotComptes depot, TimeProvider horloge) =>
            Modifier(id, version, depot, c => c.Retirer(r.Montant, r.Motif ?? "Retrait au guichet", horloge.GetUtcNow().UtcDateTime)));

        app.MapPost("/comptes/{id:guid}/cloture", (Guid id, [FromHeader(Name = "If-Match")] int? version,
                DepotComptes depot, TimeProvider horloge) =>
            Modifier(id, version, depot, c => c.Cloturer(horloge.GetUtcNow().UtcDateTime)));

        app.MapPost("/virements", async (VirementRequete r, DepotComptes depot, TimeProvider horloge) =>
        {
            if (r.Source == r.Cible)
                throw new RegleMetierException("Impossible de virer vers le même compte.");

            var (source, cible) = (await depot.Charger(r.Source), await depot.Charger(r.Cible));
            var (virementId, maintenant) = (Guid.NewGuid(), horloge.GetUtcNow().UtcDateTime);
            source.EmettreVirement(virementId, cible.Id, r.Montant, maintenant);
            cible.RecevoirVirement(virementId, source.Id, r.Montant, maintenant);

            // Deux flux modifiés, écrits en tout ou rien (si l'un a bougé entre-temps : 409, rien n'est écrit).
            await depot.Enregistrer(source, cible);
            return Results.Ok(new { virementId, soldeSource = source.Solde, soldeCible = cible.Solde });
        });
    }

    /// <param name="versionAttendue">
    /// En-tête If-Match facultatif : « je modifie le compte tel que je l'ai vu à cette version ».
    /// S'il a changé depuis, la modification est refusée (409) au lieu d'écraser une décision prise sur des données périmées.
    /// </param>
    private static async Task<IResult> Modifier(Guid id, int? versionAttendue, DepotComptes depot, Action<Compte> decision)
    {
        var compte = await depot.Charger(id);
        if (versionAttendue is not null && versionAttendue != compte.Version)
            throw new ConflitDeConcurrenceException(id, versionAttendue.Value, compte.Version);

        decision(compte);
        await depot.Enregistrer(compte);
        return Results.Ok(new { compte.Id, compte.Solde, compte.Version });
    }

    // ─── Lectures ───

    public static void MapLectures(this IEndpointRouteBuilder app)
    {
        // Vue courante, servie par une projection.
        app.MapGet("/comptes/{id:guid}", async (Guid id, MoteurProjections moteur, ProjectionReleves releves) =>
            await moteur.Lire(() => releves.Obtenir(id)) is { } releve ? Results.Ok(releve) : Results.NotFound());

        // Audit : l'historique complet et immuable du compte.
        app.MapGet("/comptes/{id:guid}/evenements", async (Guid id, IMagasinEvenements magasin) =>
            (await magasin.LireFlux(id)).Select(e => new
            {
                e.Version,
                e.Position,
                e.Type,
                e.Date,
                Donnees = JsonSerializer.Deserialize<JsonElement>(e.Donnees)
            }));

        // Voyage dans le temps : l'état à une version ou à une date passée, recalculé en rejouant les événements.
        app.MapGet("/comptes/{id:guid}/etat", async (Guid id, int? version, DateTime? date, DepotComptes depot) =>
        {
            var compte = version is not null ? await depot.ChargerALaVersion(id, version.Value)
                : date is not null ? await depot.ChargerALaDate(id, date.Value.ToUniversalTime())
                : await depot.Charger(id);
            return Results.Ok(new { compte.Id, compte.Titulaire, compte.Solde, compte.Cloture, compte.Version });
        });

        app.MapGet("/statistiques/plus-gros-retraits", (MoteurProjections moteur, ProjectionPlusGrosRetraits retraits) =>
            moteur.Lire(() => retraits.Top(5)));

        // Les projections sont jetables : on les efface et on rejoue tout le magasin.
        app.MapPost("/admin/projections/reconstruire", async (MoteurProjections moteur) =>
            Results.Ok(new { evenementsRejoues = await moteur.Reconstruire() }));

        app.MapGet("/admin/magasin", async (IMagasinEvenements magasin, MoteurProjections moteur) =>
        {
            var tout = await magasin.LireTout();
            return Results.Ok(new
            {
                nombreEvenements = tout.Count,
                nombreFlux = tout.Select(e => e.Flux).Distinct().Count(),
                parType = tout.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count()),
                positionDesProjections = moteur.Position
            });
        });
    }
}

/// <summary>Fait suivre aux projections les nouveaux événements, en continu.</summary>
internal sealed class AbonnementProjections(MoteurProjections moteur) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        while (!arret.IsCancellationRequested)
        {
            await moteur.Rattraper();
            await Task.Delay(TimeSpan.FromMilliseconds(100), arret);
        }
    }
}
