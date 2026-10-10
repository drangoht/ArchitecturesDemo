using Vertical.Api.Data;
using Vertical.Api.Domain;

namespace Vertical.Api.Features.Comptes;

/// <summary>
/// Tranche d'écriture : route + validation + logique + persistance, dans un seul fichier.
/// Pour comprendre ou modifier « retirer de l'argent », on n'ouvre que ce fichier.
/// </summary>
public static class RetirerArgent
{
    public record Requete(decimal Montant);
    public record Reponse(Guid CompteId, decimal NouveauSolde);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/comptes/{id:guid}/retrait", Executer);

    private static async Task<IResult> Executer(Guid id, Requete requete, BanqueDbContext db)
    {
        if (requete.Montant <= 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(Requete.Montant)] = ["Le montant doit être strictement positif."]
            });

        var compte = await db.Comptes.FindAsync(id);
        if (compte is null)
            return Results.NotFound();

        if (compte.Retirer(requete.Montant) is { } refus)
            return Results.BadRequest(new { erreur = refus });

        db.Operations.Add(new Operation { CompteId = id, Libelle = "Retrait", Montant = -requete.Montant });
        await db.SaveChangesAsync();

        return Results.Ok(new Reponse(id, compte.Solde));
    }
}
