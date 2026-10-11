using Ihm.Banque;
using Ihm.Mvc.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ihm.Mvc.Controllers;

/// <summary>
/// Un contrôleur MVC : une action par page. Chacune lit le modèle, prépare les données de la vue, puis désigne la vue
/// (par convention : Views/Comptes/{action}.cshtml). Le contrôleur ne produit pas de HTML et la vue ne lit pas la banque.
/// </summary>
public sealed class ComptesController(IBanque banque) : Controller
{
    // GET /  ou  /Comptes
    public async Task<IActionResult> Index(CancellationToken annulation)
    {
        var comptes = await banque.ComptesAsync(annulation);
        return View(new ListeComptes(comptes.Select(Ligne).ToList()));
    }

    // GET /Comptes/Detail/{id}
    public async Task<IActionResult> Detail(Guid id, CancellationToken annulation)
    {
        try
        {
            var compte = await banque.CompteAsync(id, annulation);
            var operations = await banque.OperationsAsync(id, annulation);
            return View(new DetailCompte(Ligne(compte), operations.Select(LigneOperation).ToList()));
        }
        catch (ErreurMetier)
        {
            return NotFound();
        }
    }

    internal static LigneCompte Ligne(Compte c) => new(c.Id, c.Titulaire, c.Libelle, Format.Euros(c.Solde));

    private static LigneOperation LigneOperation(Operation o) =>
        new(Format.Date(o.Date), o.Libelle,
            (o.Montant > 0 ? "+" : "") + Format.Euros(o.Montant), o.Montant < 0);
}
