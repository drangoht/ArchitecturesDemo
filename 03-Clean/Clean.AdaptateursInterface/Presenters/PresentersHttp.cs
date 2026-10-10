using Clean.CasUtilisation.Consultation;
using Clean.CasUtilisation.Retrait;
using Clean.CasUtilisation.Virement;

namespace Clean.AdaptateursInterface.Presenters;

// Presenters JSON : implémentent les output boundaries et décident de la forme de la réponse.

public class ConsultationPresenterHttp : IConsultationSortie
{
    public VueHttp Vue { get; private set; } = VueHttp.Refus("Aucun résultat.");

    public void Succes(ReponseConsultation r) =>
        Vue = VueHttp.Ok(new { id = r.CompteId, titulaire = r.Titulaire, solde = r.Solde });

    public void CompteIntrouvable(Guid compteId) => Vue = VueHttp.Introuvable(compteId);
}

public class RetraitPresenterHttp : IRetraitSortie
{
    public VueHttp Vue { get; private set; } = VueHttp.Refus("Aucun résultat.");

    public void Succes(ReponseRetrait r) =>
        Vue = VueHttp.Ok(new
        {
            solde = r.NouveauSolde,
            message = $"Retrait de {r.AncienSolde - r.NouveauSolde:0.00} € effectué."
        });

    public void CompteIntrouvable(Guid compteId) => Vue = VueHttp.Introuvable(compteId);
    public void Refus(string raison) => Vue = VueHttp.Refus(raison);
}

public class VirementPresenterHttp : IVirementSortie
{
    public VueHttp Vue { get; private set; } = VueHttp.Refus("Aucun résultat.");

    public void Succes(ReponseVirement r) =>
        Vue = VueHttp.Ok(new { montant = r.Montant, soldeSource = r.SoldeSource, soldeCible = r.SoldeCible });

    public void CompteIntrouvable(Guid compteId) => Vue = VueHttp.Introuvable(compteId);
    public void Refus(string raison) => Vue = VueHttp.Refus(raison);
}
