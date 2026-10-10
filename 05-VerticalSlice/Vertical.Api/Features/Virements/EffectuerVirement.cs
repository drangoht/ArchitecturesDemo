using Vertical.Api.Data;
using Vertical.Api.Domain;

namespace Vertical.Api.Features.Virements;

public static class EffectuerVirement
{
    public record Requete(Guid Source, Guid Cible, decimal Montant);
    public record Reponse(decimal SoldeSource, decimal SoldeCible);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/virements", Executer);

    private static async Task<IResult> Executer(Requete requete, BanqueDbContext db)
    {
        var erreurs = Valider(requete);
        if (erreurs.Count > 0)
            return Results.ValidationProblem(erreurs);

        var source = await db.Comptes.FindAsync(requete.Source);
        var cible = await db.Comptes.FindAsync(requete.Cible);
        if (source is null || cible is null)
            return Results.NotFound();

        // Règle partagée avec RetirerArgent → elle vit dans l'entité Compte.
        if (source.Retirer(requete.Montant) is { } refus)
            return Results.BadRequest(new { erreur = refus });
        cible.Deposer(requete.Montant);

        db.Operations.AddRange(
            new Operation { CompteId = source.Id, Libelle = $"Virement vers {cible.Titulaire}", Montant = -requete.Montant },
            new Operation { CompteId = cible.Id, Libelle = $"Virement de {source.Titulaire}", Montant = requete.Montant });
        await db.SaveChangesAsync();

        return Results.Ok(new Reponse(source.Solde, cible.Solde));
    }

    private static Dictionary<string, string[]> Valider(Requete requete)
    {
        var erreurs = new Dictionary<string, string[]>();
        if (requete.Montant <= 0)
            erreurs[nameof(Requete.Montant)] = ["Le montant doit être strictement positif."];
        if (requete.Source == requete.Cible)
            erreurs[nameof(Requete.Cible)] = ["Impossible de virer vers le même compte."];
        return erreurs;
    }
}
