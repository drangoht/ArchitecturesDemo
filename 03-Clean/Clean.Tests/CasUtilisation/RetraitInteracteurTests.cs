using Clean.AdaptateursInterface.Passerelles;
using Clean.CasUtilisation.Retrait;
using Clean.Entites;

namespace Clean.Tests.CasUtilisation;

// L'interacteur se teste avec un espion sur l'output boundary : on vérifie CE QU'IL DIT, pas un format.
public class RetraitInteracteurTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private readonly ComptesPasserelleEnMemoire _comptes = new(new Compte(Alice, "Alice", 100m));
    private readonly SortieEspion _sortie = new();

    [Fact]
    public async Task Un_retrait_valide_annonce_l_ancien_et_le_nouveau_solde()
    {
        await new RetraitInteracteur(_comptes).Executer(new DemandeRetrait(Alice, 30m), _sortie);

        Assert.Equal(new ReponseRetrait(Alice, 100m, 70m), _sortie.Succes);
        Assert.Equal(70m, (await _comptes.Charger(Alice))!.Solde);
    }

    [Fact]
    public async Task Un_retrait_trop_eleve_annonce_un_refus()
    {
        await new RetraitInteracteur(_comptes).Executer(new DemandeRetrait(Alice, 500m), _sortie);

        Assert.Equal("Solde insuffisant.", _sortie.Refus);
        Assert.Null(_sortie.Succes);
    }

    [Fact]
    public async Task Un_compte_inconnu_est_annonce_comme_introuvable()
    {
        var inconnu = Guid.NewGuid();

        await new RetraitInteracteur(_comptes).Executer(new DemandeRetrait(inconnu, 10m), _sortie);

        Assert.Equal(inconnu, _sortie.Introuvable);
    }

    private class SortieEspion : IRetraitSortie
    {
        public ReponseRetrait? Succes { get; private set; }
        public Guid? Introuvable { get; private set; }
        public string? Refus { get; private set; }

        void IRetraitSortie.Succes(ReponseRetrait reponse) => Succes = reponse;
        void IRetraitSortie.CompteIntrouvable(Guid compteId) => Introuvable = compteId;
        void IRetraitSortie.Refus(string raison) => Refus = raison;
    }
}
