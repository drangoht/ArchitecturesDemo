using Clean.CasUtilisation.Passerelles;
using Clean.Entites;

namespace Clean.CasUtilisation.Virement;

public record DemandeVirement(Guid SourceId, Guid CibleId, decimal Montant);
public record ReponseVirement(decimal Montant, decimal SoldeSource, decimal SoldeCible);

public interface IVirementEntree
{
    Task Executer(DemandeVirement demande, IVirementSortie sortie);
}

public interface IVirementSortie
{
    void Succes(ReponseVirement reponse);
    void CompteIntrouvable(Guid compteId);
    void Refus(string raison);
}

public class VirementInteracteur(IComptesPasserelle comptes) : IVirementEntree
{
    public async Task Executer(DemandeVirement demande, IVirementSortie sortie)
    {
        if (demande.SourceId == demande.CibleId)
        {
            sortie.Refus("Impossible de virer vers le même compte.");
            return;
        }

        var source = await comptes.Charger(demande.SourceId);
        if (source is null)
        {
            sortie.CompteIntrouvable(demande.SourceId);
            return;
        }

        var cible = await comptes.Charger(demande.CibleId);
        if (cible is null)
        {
            sortie.CompteIntrouvable(demande.CibleId);
            return;
        }

        try
        {
            source.Retirer(demande.Montant);
            cible.Deposer(demande.Montant);
        }
        catch (RegleMetierException e)
        {
            sortie.Refus(e.Message);
            return;
        }

        await comptes.Sauvegarder(source);
        await comptes.Sauvegarder(cible);
        sortie.Succes(new ReponseVirement(demande.Montant, source.Solde, cible.Solde));
    }
}
