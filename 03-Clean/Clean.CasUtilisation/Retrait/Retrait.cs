using Clean.CasUtilisation.Passerelles;
using Clean.Entites;

namespace Clean.CasUtilisation.Retrait;

public record DemandeRetrait(Guid CompteId, decimal Montant);
public record ReponseRetrait(Guid CompteId, decimal AncienSolde, decimal NouveauSolde);

public interface IRetraitEntree
{
    Task Executer(DemandeRetrait demande, IRetraitSortie sortie);
}

public interface IRetraitSortie
{
    void Succes(ReponseRetrait reponse);
    void CompteIntrouvable(Guid compteId);
    void Refus(string raison);
}

/// <summary>
/// L'interacteur ne renvoie rien : il pousse son résultat vers l'output boundary.
/// Il ignore donc totalement si la réponse finira en JSON, en HTML ou en texte.
/// </summary>
public class RetraitInteracteur(IComptesPasserelle comptes) : IRetraitEntree
{
    public async Task Executer(DemandeRetrait demande, IRetraitSortie sortie)
    {
        var compte = await comptes.Charger(demande.CompteId);
        if (compte is null)
        {
            sortie.CompteIntrouvable(demande.CompteId);
            return;
        }

        var ancienSolde = compte.Solde;
        try
        {
            compte.Retirer(demande.Montant);
        }
        catch (RegleMetierException e)
        {
            sortie.Refus(e.Message);
            return;
        }

        await comptes.Sauvegarder(compte);
        sortie.Succes(new ReponseRetrait(compte.Id, ancienSolde, compte.Solde));
    }
}
