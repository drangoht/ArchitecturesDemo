using Ihm.Banque;
using Ihm.Mvc.Controllers;
using Ihm.Mvc.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Ihm.Tests;

/// <summary>
/// Les contrôleurs MVC appelés comme de simples méthodes : on vérifie la DÉCISION (quelle vue, quelles données, quelle
/// redirection), pas le HTML. Ce qui se passe avant l'action (liaison, validation) n'a pas lieu ici : ModelState est
/// rempli à la main, et c'est tout l'intérêt des tests de SiteTests, qui passent par le pipeline complet.
/// </summary>
public class ControleurTests
{
    private readonly BanqueEnMemoire _banque = new(new HorlogeFixe());

    private VirementsController Virements() => new(_banque)
    {
        TempData = new TempDataDictionary(new DefaultHttpContext(), new TempDataSansStockage()),
    };

    [Fact]
    public async Task La_liste_choisit_sa_vue_et_lui_passe_des_donnees_pretes_a_afficher()
    {
        var resultat = Assert.IsType<ViewResult>(await new ComptesController(_banque).Index(default));

        Assert.Null(resultat.ViewName);   // la vue par convention : Views/Comptes/Index.cshtml
        var modele = Assert.IsType<ListeComptes>(resultat.Model);
        Assert.Equal(["1 000,00 €", "8 000,00 €", "500,00 €"], modele.Comptes.Select(c => c.Solde));
    }

    [Fact]
    public async Task Un_compte_inconnu_donne_404()
    {
        Assert.IsType<NotFoundResult>(await new ComptesController(_banque).Detail(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Un_formulaire_invalide_est_reaffiche_sans_rien_virer()
    {
        var controleur = Virements();
        // Un formulaire complet, mais où le pipeline a relevé une erreur : l'action doit s'en tenir à ModelState.
        controleur.ModelState.AddModelError("Montant", "Le montant se donne au centime près.");
        var formulaire = new FormulaireVirement { Source = BanqueEnMemoire.Alice, Cible = BanqueEnMemoire.Bob, Montant = 250m };

        var resultat = Assert.IsType<ViewResult>(await controleur.Nouveau(formulaire, default));

        Assert.Equal("Nouveau", resultat.ViewName);
        Assert.Equal(3, Assert.IsType<FormulaireVirement>(resultat.Model).Comptes.Count);   // listes rechargées
        Assert.Equal(1_000m, (await _banque.CompteAsync(BanqueEnMemoire.Alice)).Solde);
    }

    [Fact]
    public async Task Un_virement_reussi_redirige_vers_le_compte_avec_un_message()
    {
        var controleur = Virements();
        var formulaire = new FormulaireVirement { Source = BanqueEnMemoire.Alice, Cible = BanqueEnMemoire.Bob, Montant = 250.50m };

        var resultat = Assert.IsType<RedirectToActionResult>(await controleur.Nouveau(formulaire, default));

        Assert.Equal(("Comptes", "Detail"), (resultat.ControllerName, resultat.ActionName));
        Assert.Equal(BanqueEnMemoire.Alice, resultat.RouteValues!["id"]);
        Assert.Equal("Virement de 250,50 € effectué. Nouveau solde : 749,50 €.", controleur.TempData["Message"]);
    }

    [Fact]
    public async Task Un_gros_montant_passe_par_la_vue_de_confirmation()
    {
        var formulaire = new FormulaireVirement { Source = BanqueEnMemoire.LivretAlice, Cible = BanqueEnMemoire.Bob, Montant = 1_500m };

        var resultat = Assert.IsType<ViewResult>(await Virements().Nouveau(formulaire, default));

        Assert.Equal("Confirmer", resultat.ViewName);
        Assert.Equal(8_000m, (await _banque.CompteAsync(BanqueEnMemoire.LivretAlice)).Solde);
    }

    [Fact]
    public async Task Un_refus_de_la_banque_devient_une_erreur_du_formulaire()
    {
        var controleur = Virements();
        var formulaire = new FormulaireVirement { Source = BanqueEnMemoire.Bob, Cible = BanqueEnMemoire.Alice, Montant = 600m };

        var resultat = Assert.IsType<ViewResult>(await controleur.Nouveau(formulaire, default));

        Assert.Equal("Nouveau", resultat.ViewName);
        Assert.Equal("Solde insuffisant : 500,00 € disponibles.", Assert.Single(controleur.ModelState[string.Empty]!.Errors).ErrorMessage);
    }

    private sealed class TempDataSansStockage : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
