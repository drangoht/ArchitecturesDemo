using Hexa.Hexagone.Domaine;
using Hexa.Hexagone.Ports.Entrants;

namespace Hexa.Adapters.Web;

/// <summary>
/// Adapter entrant HTTP : traduit HTTP ⇄ ports entrants. Aucune règle métier ici.
/// </summary>
public static class ComptesEndpoints
{
    public record RetraitRequete(decimal Montant);
    public record VirementRequete(Guid Source, Guid Cible, decimal Montant);

    public static void MapComptesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/comptes/{id:guid}", (Guid id, IConsulterCompte consulter) =>
            Traduire(async () => Results.Ok(await consulter.Executer(id))));

        app.MapPost("/comptes/{id:guid}/retrait", (Guid id, RetraitRequete requete, IRetirerArgent retirer) =>
            Traduire(async () =>
            {
                await retirer.Executer(id, requete.Montant);
                return Results.NoContent();
            }));

        app.MapPost("/virements", (VirementRequete requete, IEffectuerVirement virer) =>
            Traduire(async () =>
            {
                await virer.Executer(requete.Source, requete.Cible, requete.Montant);
                return Results.NoContent();
            }));
    }

    // Traduire les erreurs du métier en codes HTTP est le rôle de l'adapter, pas de l'hexagone.
    private static async Task<IResult> Traduire(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CompteIntrouvableException e)
        {
            return Results.NotFound(new { erreur = e.Message });
        }
        catch (RegleMetierException e)
        {
            return Results.BadRequest(new { erreur = e.Message });
        }
    }
}
