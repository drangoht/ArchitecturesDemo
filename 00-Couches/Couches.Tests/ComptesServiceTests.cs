using Couches.Donnees;
using Couches.Metier;

namespace Couches.Tests;

/// <summary>
/// Tester la couche métier oblige à manipuler la couche données : même le faux DAO
/// renvoie des CompteEntite. Le test du métier est couplé au schéma de persistance.
/// </summary>
public class ComptesServiceTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly FauxDao _dao = new(
        new CompteEntite { Id = Alice, Titulaire = "Alice", Solde = 100m },
        new CompteEntite { Id = Bob, Titulaire = "Bob", Solde = 0m });

    private ComptesService Service => new(_dao);

    [Fact]
    public async Task Un_retrait_valide_debite_et_enregistre()
    {
        var compte = await Service.Retirer(Alice, 30m);

        Assert.Equal(70m, compte.Solde);
        Assert.Equal(1, _dao.NombreEnregistrements);
    }

    [Fact]
    public async Task Un_retrait_trop_eleve_est_refuse_sans_enregistrement()
    {
        await Assert.ThrowsAsync<RegleMetierException>(() => Service.Retirer(Alice, 500m));

        Assert.Equal(0, _dao.NombreEnregistrements);
    }

    [Fact]
    public async Task Un_virement_deplace_l_argent()
    {
        await Service.Virer(Alice, Bob, 40m);

        Assert.Equal(60m, (await _dao.ParId(Alice))!.Solde);
        Assert.Equal(40m, (await _dao.ParId(Bob))!.Solde);
    }

    [Fact]
    public async Task Un_virement_au_dela_du_plafond_est_refuse()
    {
        await Assert.ThrowsAsync<RegleMetierException>(() => Service.Virer(Alice, Bob, 6_000m));
    }

    [Fact]
    public async Task Un_compte_inconnu_leve_CompteIntrouvable()
    {
        await Assert.ThrowsAsync<CompteIntrouvableException>(() => Service.Consulter(Guid.NewGuid()));
    }

    private class FauxDao(params CompteEntite[] comptes) : IComptesDao
    {
        private readonly List<CompteEntite> _comptes = [.. comptes];
        public int NombreEnregistrements { get; private set; }

        public Task<CompteEntite?> ParId(Guid id) => Task.FromResult(_comptes.FirstOrDefault(c => c.Id == id));
        public Task Ajouter(CompteEntite compte) { _comptes.Add(compte); return Task.CompletedTask; }
        public Task<bool> EstVide() => Task.FromResult(_comptes.Count == 0);
        public Task Enregistrer() { NombreEnregistrements++; return Task.CompletedTask; }
    }
}
