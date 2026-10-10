using System.Diagnostics;
using Fonctionnel.Coeur;
using Fonctionnel.Coquille.Donnees;
using Microsoft.EntityFrameworkCore;

namespace Fonctionnel.Coquille;

public static class Endpoints
{
    public record RetraitRequete(decimal Montant);
    public record VirementRequete(Guid Source, Guid Cible, decimal Montant);

    public static void MapBanque(this IEndpointRouteBuilder app)
    {
        app.MapGet("/comptes/{id:guid}", Consulter);
        app.MapPost("/comptes/{id:guid}/retrait", Retirer);
        app.MapPost("/virements", Virer);
    }

    private static async Task<IResult> Consulter(Guid id, BanqueDbContext db)
    {
        var ligne = await db.Comptes.FindAsync(id);
        return ligne is null ? Results.NotFound() : Results.Ok(ligne.VersCoeur());
    }

    private static async Task<IResult> Retirer(
        Guid id, RetraitRequete requete, BanqueDbContext db, TimeProvider horloge, ILoggerFactory journaux)
    {
        // 1. LIRE (I/O)
        var ligne = await db.Comptes.FindAsync(id);
        if (ligne is null)
            return Results.NotFound();

        // 2. DÉCIDER (pur)
        var decision = Regles.Retirer(ligne.VersCoeur(), requete.Montant);

        // 3. APPLIQUER (I/O)
        switch (decision)
        {
            case DecisionRetrait.Accepte ok:
                ligne.Solde = ok.CompteMisAJour.Solde;
                db.Operations.Add(Operation(id, "Retrait", ok.Montant, horloge));
                await db.SaveChangesAsync();
                journaux.CreateLogger("Banque").LogInformation("Retrait de {Montant} € sur {Compte}", ok.Montant, id);
                return Results.Ok(ok.CompteMisAJour);

            case DecisionRetrait.Refuse refus:
                return Results.BadRequest(new { erreur = refus.Raison });

            default:
                throw new UnreachableException();
        }
    }

    private static async Task<IResult> Virer(
        VirementRequete requete, BanqueDbContext db, TimeProvider horloge, ILoggerFactory journaux)
    {
        // 1. LIRE (I/O) : les comptes ET tout ce dont la règle a besoin (ici, le cumul du jour).
        var source = await db.Comptes.FindAsync(requete.Source);
        var cible = await db.Comptes.FindAsync(requete.Cible);
        if (source is null || cible is null)
            return Results.NotFound();

        var debutDuJour = horloge.GetUtcNow().UtcDateTime.Date;
        var dejaVire = await db.Operations
            .Where(o => o.CompteId == source.Id && o.Type == "VirementEmis" && o.DateUtc >= debutDuJour)
            .SumAsync(o => o.Montant);

        // 2. DÉCIDER (pur)
        var decision = Regles.Virer(source.VersCoeur(), cible.VersCoeur(), requete.Montant, dejaVire);

        // 3. APPLIQUER (I/O)
        switch (decision)
        {
            case DecisionVirement.Accepte ok:
                source.Solde = ok.Source.Solde;
                cible.Solde = ok.Cible.Solde;
                db.Operations.Add(Operation(source.Id, "VirementEmis", ok.Montant, horloge));
                db.Operations.Add(Operation(cible.Id, "VirementRecu", ok.Montant, horloge));
                await db.SaveChangesAsync();
                journaux.CreateLogger("Banque").LogInformation(
                    "Virement de {Montant} € de {Source} vers {Cible}", ok.Montant, source.Id, cible.Id);
                return Results.Ok(new { source = ok.Source, cible = ok.Cible });

            case DecisionVirement.Refuse refus:
                return Results.BadRequest(new { erreur = refus.Raison });

            default:
                throw new UnreachableException();
        }
    }

    private static OperationLigne Operation(Guid compteId, string type, decimal montant, TimeProvider horloge) =>
        new() { CompteId = compteId, Type = type, Montant = montant, DateUtc = horloge.GetUtcNow().UtcDateTime };
}
