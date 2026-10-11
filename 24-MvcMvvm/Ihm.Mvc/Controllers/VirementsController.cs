using Ihm.Banque;
using Ihm.Mvc.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ihm.Mvc.Controllers;

/// <summary>
/// Le formulaire de virement, en trois temps typiques du MVC web :
/// GET affiche le formulaire ; POST le valide et, s'il est faux, RÉAFFICHE la même vue avec les erreurs (200) ;
/// s'il est bon, exécute puis REDIRIGE (302, « Post/Redirect/Get ») : actualiser la page d'arrivée ne refait pas le virement.
/// </summary>
public sealed class VirementsController(IBanque banque) : Controller
{
    // GET /Virements/Nouveau?source={id}
    [HttpGet]
    public async Task<IActionResult> Nouveau(Guid? source, CancellationToken annulation)
    {
        var formulaire = new FormulaireVirement { Source = source };
        await ChargerComptes(formulaire, annulation);
        return View(formulaire);
    }

    // POST /Virements/Nouveau  (champs du formulaire + jeton anti-falsification, vérifié par le filtre global)
    [HttpPost]
    public async Task<IActionResult> Nouveau(FormulaireVirement formulaire, CancellationToken annulation)
    {
        // La liaison de modèle a déjà rempli le formulaire et noté dans ModelState ce qui ne va pas :
        // champ manquant, « abc » au lieu d'un nombre, règles des attributs et de Validate.
        if (!ModelState.IsValid)
            return await Reafficher(formulaire, annulation);

        var (source, cible, montant) = (formulaire.Source!.Value, formulaire.Cible!.Value, formulaire.Montant!.Value);

        // Une étape de plus pour les gros montants : une autre vue, qui renvoie les mêmes champs (cachés) et Confirme=true.
        if (montant >= ReglesVirement.SeuilConfirmation && !formulaire.Confirme)
        {
            await ChargerComptes(formulaire, annulation);
            return View("Confirmer", formulaire);
        }

        try
        {
            var virement = await banque.VirerAsync(source, cible, montant, annulation);
            // TempData survit à UNE redirection (cookie), puis disparaît : le message s'affiche une seule fois.
            TempData["Message"] = $"Virement de {Format.Euros(virement.Montant)} effectué. Nouveau solde : {Format.Euros(virement.SoldeSource)}.";
            return RedirectToAction(nameof(ComptesController.Detail), "Comptes", new { id = source });
        }
        catch (ErreurMetier erreur)
        {
            // Refus de la banque (solde insuffisant…) : une erreur sans champ, affichée en tête du formulaire.
            ModelState.AddModelError(string.Empty, erreur.Message);
            formulaire.Confirme = false;
            return await Reafficher(formulaire, annulation);
        }
    }

    private async Task<IActionResult> Reafficher(FormulaireVirement formulaire, CancellationToken annulation)
    {
        await ChargerComptes(formulaire, annulation);
        return View(nameof(Nouveau), formulaire);
    }

    private async Task ChargerComptes(FormulaireVirement formulaire, CancellationToken annulation) =>
        formulaire.Comptes = (await banque.ComptesAsync(annulation)).Select(ComptesController.Ligne).ToList();
}
