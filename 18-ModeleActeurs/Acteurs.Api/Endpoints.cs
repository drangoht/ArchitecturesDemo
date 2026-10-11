using System.Diagnostics;
using System.Text.RegularExpressions;
using Acteurs.Banque;
using Acteurs.Noyau;

namespace Acteurs.Api;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

internal static partial class Endpoints
{
    internal record OuvertureRequete(string Titulaire, decimal DepotInitial);
    internal record MontantRequete(decimal Montant);
    internal record VirementRequete(string? Id, Guid Source, Guid Cible, decimal Montant);
    internal record RetraitsSimultanesRequete(Guid Compte, int Nombre, decimal Montant);
    internal record VirementsCroisesRequete(int Nombre, decimal Montant);

    [GeneratedRegex("^[A-Za-z0-9-]{1,40}$")]
    private static partial Regex IdentifiantValide();

    /// <summary>
    /// Chaque route traduit la requête HTTP en UN message pour UN acteur, et sa réponse en code HTTP.
    /// L'API ne lit ni n'écrit aucun état : elle ne fait que parler aux acteurs.
    /// </summary>
    public static void MapBanque(this IEndpointRouteBuilder app)
    {
        app.MapPost("/comptes", async (OuvertureRequete r, SystemeActeurs systeme) =>
        {
            var id = Guid.NewGuid();
            var reponse = await systeme.Compte(id).Demander(new OuvrirCompte(r.Titulaire, r.DepotInitial));
            return reponse is CompteDto compte ? Results.Created($"/comptes/{id}", compte) : Http(reponse);
        });

        app.MapGet("/comptes/{id:guid}", async (Guid id, SystemeActeurs systeme) =>
            Http(await systeme.Compte(id).Demander(new ConsulterCompte())));

        app.MapGet("/comptes/{id:guid}/operations", async (Guid id, SystemeActeurs systeme) =>
            Http(await systeme.Compte(id).Demander(new ListerOperations())));

        app.MapPost("/comptes/{id:guid}/depot", async (Guid id, MontantRequete r, SystemeActeurs systeme) =>
            Http(await systeme.Compte(id).Demander(new Deposer(r.Montant))));

        app.MapPost("/comptes/{id:guid}/retrait", async (Guid id, MontantRequete r, SystemeActeurs systeme) =>
            Http(await systeme.Compte(id).Demander(new Retirer(r.Montant))));

        // L'identifiant du virement, s'il est fourni par le client, rend la requête rejouable sans risque.
        app.MapPost("/virements", async (VirementRequete r, SystemeActeurs systeme) =>
        {
            var id = r.Id ?? $"v-{Guid.NewGuid():N}"[..14];
            if (!IdentifiantValide().IsMatch(id))
                return Results.BadRequest(new { erreur = "Identifiant de virement : 1 à 40 lettres, chiffres ou tirets." });
            return Http(await systeme.Virement(id).Demander(new ExecuterVirement(r.Source, r.Cible, r.Montant)));
        });

        app.MapGet("/virements/{id}", async (string id, SystemeActeurs systeme) =>
            IdentifiantValide().IsMatch(id) ? Http(await systeme.Virement(id).Demander(new ConsulterVirement())) : Results.NotFound());
    }

    /// <summary>Observer le runtime et provoquer des pannes.</summary>
    public static void MapSysteme(this IEndpointRouteBuilder app)
    {
        var groupe = app.MapGroup("/systeme");

        groupe.MapGet("/", (SystemeActeurs systeme) => Results.Ok(systeme.Etat()));

        groupe.MapGet("/journal", (SystemeActeurs systeme) => Results.Ok(systeme.Journal()));

        // Le stockage durable : un acteur passivé n'est plus en mémoire, mais sa ligne est toujours là.
        groupe.MapGet("/stockage", (string? type, MagasinEtats magasin) =>
            Results.Ok(magasin.Chemins().Where(c => type is null || c.StartsWith($"{type}/"))));

        groupe.MapGet("/pannes", (Pieges pieges) => Results.Ok(pieges.Armes()));

        groupe.MapPost("/pannes", (Piege piege, Pieges pieges) =>
        {
            pieges.Armer(piege);
            return Results.Ok(pieges.Armes());
        });

        groupe.MapDelete("/pannes", (Pieges pieges) =>
        {
            pieges.ToutDesarmer();
            return Results.NoContent();
        });

        // Sans attendre la passivation automatique : endormir tout acteur inactif depuis N secondes (0 = tous les inactifs).
        groupe.MapPost("/passivation", (int? inactiviteSecondes, SystemeActeurs systeme) =>
            Results.Ok(new { passives = systeme.Passiver(TimeSpan.FromSeconds(inactiviteSecondes ?? 0)) }));
    }

    /// <summary>Les démonstrations de concurrence : beaucoup de messages en même temps, aucun verrou dans le métier.</summary>
    public static void MapDemonstrations(this IEndpointRouteBuilder app)
    {
        app.MapPost("/demo/retraits-simultanes", async (RetraitsSimultanesRequete r, SystemeActeurs systeme) =>
        {
            if (r.Nombre is < 1 or > 10_000)
                return Results.BadRequest(new { erreur = "Nombre : de 1 à 10 000." });
            var compte = systeme.Compte(r.Compte);
            if (await compte.Demander(new ConsulterCompte()) is not CompteDto avant)
                return Results.NotFound();

            var chrono = Stopwatch.StartNew();
            var reponses = await Task.WhenAll(Enumerable.Range(0, r.Nombre)
                .Select(_ => Task.Run(() => compte.Demander(new Retirer(r.Montant)))));
            var acceptes = reponses.OfType<CompteDto>().ToList();

            return Results.Ok(new
            {
                soldeAvant = avant.Solde,
                demandes = r.Nombre,
                acceptes = acceptes.Count,
                refuses = reponses.OfType<Refus>().Count(),
                soldeFinal = ((CompteDto)(await compte.Demander(new ConsulterCompte()))!).Solde,
                // Chaque réponse acceptée porte le solde juste après SON retrait : jamais négatif.
                soldeMinimumObserve = acceptes.Count > 0 ? acceptes.Min(c => c.Solde) : avant.Solde,
                dureeMs = chrono.ElapsedMilliseconds
            });
        });

        app.MapPost("/demo/virements-croises", async (VirementsCroisesRequete r, SystemeActeurs systeme) =>
        {
            if (r.Nombre is < 1 or > 5_000)
                return Results.BadRequest(new { erreur = "Nombre : de 1 à 5 000 dans chaque sens." });

            var avant = await Total(systeme);
            var chrono = Stopwatch.StartNew();
            var serie = Guid.NewGuid().ToString("N")[..6];
            var virements = Enumerable.Range(0, r.Nombre).SelectMany(i => new[]
            {
                (Id: $"x{serie}-ab-{i}", Source: DonneesDeDemo.Alice, Cible: DonneesDeDemo.Bob),
                (Id: $"x{serie}-ba-{i}", Source: DonneesDeDemo.Bob, Cible: DonneesDeDemo.Alice)
            });
            var reponses = await Task.WhenAll(virements.Select(v => Task.Run(() =>
                systeme.Virement(v.Id).Demander(new ExecuterVirement(v.Source, v.Cible, r.Montant)))));

            return Results.Ok(new
            {
                virements = reponses.Length,
                termines = reponses.OfType<VirementDto>().Count(v => v.Statut == StatutVirement.Termine),
                refuses = reponses.OfType<VirementDto>().Count(v => v.Statut == StatutVirement.Refuse),
                totalAvant = avant,
                totalApres = await Total(systeme),  // l'argent ne se crée ni ne se perd
                dureeMs = chrono.ElapsedMilliseconds
            });
        });
    }

    private static async Task<decimal> Total(SystemeActeurs systeme)
    {
        var alice = (CompteDto)(await systeme.Compte(DonneesDeDemo.Alice).Demander(new ConsulterCompte()))!;
        var bob = (CompteDto)(await systeme.Compte(DonneesDeDemo.Bob).Demander(new ConsulterCompte()))!;
        return alice.Solde + bob.Solde;
    }

    /// <summary>La réponse d'un acteur → HTTP.</summary>
    private static IResult Http(object? reponse) => reponse switch
    {
        Refus { Introuvable: true } r => Results.NotFound(new { erreur = r.Motif }),
        Refus r => Results.BadRequest(new { erreur = r.Motif }),
        _ => Results.Ok(reponse)
    };
}
