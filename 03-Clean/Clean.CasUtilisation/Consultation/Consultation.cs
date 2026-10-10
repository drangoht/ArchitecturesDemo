using Clean.CasUtilisation.Passerelles;

namespace Clean.CasUtilisation.Consultation;

// Request model / Response model : de simples structures de données, sans dépendance au web.
public record DemandeConsultation(Guid CompteId);
public record ReponseConsultation(Guid CompteId, string Titulaire, decimal Solde);

// Input boundary : ce que le contrôleur appelle.
public interface IConsultationEntree
{
    Task Executer(DemandeConsultation demande, IConsultationSortie sortie);
}

// Output boundary : ce que l'interacteur appelle pour rendre son résultat (implémenté par un presenter).
public interface IConsultationSortie
{
    void Succes(ReponseConsultation reponse);
    void CompteIntrouvable(Guid compteId);
}

// Interactor : le cas d'utilisation lui-même.
public class ConsultationInteracteur(IComptesPasserelle comptes) : IConsultationEntree
{
    public async Task Executer(DemandeConsultation demande, IConsultationSortie sortie)
    {
        var compte = await comptes.Charger(demande.CompteId);
        if (compte is null)
        {
            sortie.CompteIntrouvable(demande.CompteId);
            return;
        }

        sortie.Succes(new ReponseConsultation(compte.Id, compte.Titulaire, compte.Solde));
    }
}
