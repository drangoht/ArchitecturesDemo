using Clean.CasUtilisation.Consultation;
using Clean.CasUtilisation.Retrait;
using Clean.CasUtilisation.Virement;

namespace Clean.AdaptateursInterface.Controleurs;

/// <summary>
/// Contrôleur : convertit une entrée brute (paramètres de route, corps JSON…) en request model
/// et appelle l'input boundary. Il ne lit jamais le résultat : c'est le travail du presenter.
/// </summary>
public class ComptesControleur(IConsultationEntree consultation, IRetraitEntree retrait, IVirementEntree virement)
{
    public Task Consulter(Guid compteId, IConsultationSortie presenter) =>
        consultation.Executer(new DemandeConsultation(compteId), presenter);

    public Task Retirer(Guid compteId, decimal montant, IRetraitSortie presenter) =>
        retrait.Executer(new DemandeRetrait(compteId, montant), presenter);

    public Task Virer(Guid sourceId, Guid cibleId, decimal montant, IVirementSortie presenter) =>
        virement.Executer(new DemandeVirement(sourceId, cibleId, montant), presenter);
}
