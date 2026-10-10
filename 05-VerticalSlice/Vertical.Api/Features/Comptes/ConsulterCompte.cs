using Microsoft.EntityFrameworkCore;
using Vertical.Api.Data;

namespace Vertical.Api.Features.Comptes;

/// <summary>
/// Tranche de lecture : projection directe vers la réponse, sans charger l'entité ni passer par le domaine.
/// Chaque tranche choisit la technique qui lui convient (ici une requête LINQ optimisée en lecture seule).
/// </summary>
public static class ConsulterCompte
{
    public record Reponse(Guid Id, string Titulaire, decimal Solde, List<OperationVue> DernieresOperations);
    public record OperationVue(string Libelle, decimal Montant, DateTime DateUtc);

    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/comptes/{id:guid}", Executer);

    private static async Task<IResult> Executer(Guid id, BanqueDbContext db)
    {
        var compte = await db.Comptes
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Titulaire, c.Solde })
            .FirstOrDefaultAsync();

        if (compte is null)
            return Results.NotFound();

        var operations = await db.Operations
            .AsNoTracking()
            .Where(o => o.CompteId == id)
            .OrderByDescending(o => o.DateUtc)
            .Take(5)
            .Select(o => new OperationVue(o.Libelle, o.Montant, o.DateUtc))
            .ToListAsync();

        return Results.Ok(new Reponse(compte.Id, compte.Titulaire, compte.Solde, operations));
    }
}
