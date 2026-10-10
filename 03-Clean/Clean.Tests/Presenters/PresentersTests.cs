using Clean.AdaptateursInterface.Presenters;
using Clean.CasUtilisation.Consultation;
using Clean.CasUtilisation.Retrait;

namespace Clean.Tests.Presenters;

// Les presenters se testent seuls, sans interacteur : on leur donne une réponse, on lit la vue.
public class PresentersTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Fact]
    public void Le_presenter_texte_produit_un_releve_lisible()
    {
        var presenter = new ConsultationPresenterTexte();

        presenter.Succes(new ReponseConsultation(Id, "Alice", 1234.5m));

        Assert.Equal(200, presenter.Statut);
        Assert.Contains("Titulaire : Alice", presenter.Texte);
        Assert.Contains("234,50 €", presenter.Texte);
    }

    [Fact]
    public void Le_presenter_http_traduit_un_refus_en_400()
    {
        var presenter = new RetraitPresenterHttp();

        ((IRetraitSortie)presenter).Refus("Solde insuffisant.");

        Assert.Equal(400, presenter.Vue.Statut);
    }

    [Fact]
    public void Le_presenter_http_traduit_un_compte_introuvable_en_404()
    {
        var presenter = new ConsultationPresenterHttp();

        presenter.CompteIntrouvable(Id);

        Assert.Equal(404, presenter.Vue.Statut);
    }
}
