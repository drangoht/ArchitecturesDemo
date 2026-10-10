using Vertical.Api.Data;
using Vertical.Api.Domain;

namespace Vertical.Api.Features.Comptes;

/// <summary>
/// Tranche la plus simple possible (« transaction script ») :
/// pas de règle partagée, donc tout tient ici, sans abstraction.
/// </summary>
public static class OuvrirCompte
{
    public record Requete(string Titulaire, decimal DepotInitial);
    public record Reponse(Guid Id, string Titulaire, decimal Solde);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/comptes", Executer);

    private static async Task<IResult> Executer(Requete requete, BanqueDbContext db)
    {
        var erreurs = Valider(requete);
        if (erreurs.Count > 0)
            return Results.ValidationProblem(erreurs);

        var compte = new Compte(Guid.NewGuid(), requete.Titulaire.Trim(), requete.DepotInitial);
        db.Comptes.Add(compte);
        db.Operations.Add(new Operation { CompteId = compte.Id, Libelle = "Dépôt initial", Montant = requete.DepotInitial });
        await db.SaveChangesAsync();

        return Results.Created($"/comptes/{compte.Id}", new Reponse(compte.Id, compte.Titulaire, compte.Solde));
    }

    private static Dictionary<string, string[]> Valider(Requete requete)
    {
        var erreurs = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(requete.Titulaire))
            erreurs[nameof(Requete.Titulaire)] = ["Le titulaire est obligatoire."];
        if (requete.DepotInitial < 10)
            erreurs[nameof(Requete.DepotInitial)] = ["Le dépôt initial doit être d'au moins 10 €."];
        return erreurs;
    }
}
