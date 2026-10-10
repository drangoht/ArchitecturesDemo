using Couches.Metier;
using Microsoft.AspNetCore.Mvc;

namespace Couches.Presentation.Controllers;

public record RetraitRequete(decimal Montant);
public record VirementRequete(Guid Source, Guid Cible, decimal Montant);

/// <summary>
/// Contrôleur classique : il reçoit la requête, appelle la couche métier et traduit les erreurs en HTTP.
/// Il ne voit que la couche juste en dessous (Couches.Metier).
/// </summary>
[ApiController]
[Route("")]
public class ComptesController(ComptesService comptes) : ControllerBase
{
    [HttpGet("comptes/{id:guid}")]
    public Task<IActionResult> Consulter(Guid id) =>
        Executer(async () => Ok(await comptes.Consulter(id)));

    [HttpPost("comptes/{id:guid}/retrait")]
    public Task<IActionResult> Retirer(Guid id, RetraitRequete requete) =>
        Executer(async () => Ok(await comptes.Retirer(id, requete.Montant)));

    [HttpPost("virements")]
    public Task<IActionResult> Virer(VirementRequete requete) =>
        Executer(async () =>
        {
            await comptes.Virer(requete.Source, requete.Cible, requete.Montant);
            return NoContent();
        });

    private async Task<IActionResult> Executer(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CompteIntrouvableException e)
        {
            return NotFound(new { erreur = e.Message });
        }
        catch (RegleMetierException e)
        {
            return BadRequest(new { erreur = e.Message });
        }
    }
}
