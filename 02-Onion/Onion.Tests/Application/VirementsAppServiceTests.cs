using Onion.Application;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;
using Onion.Domain.Services;

namespace Onion.Tests.Application;

// La couche application se teste avec de faux repository / unit of work : pas d'EF Core ici.
public class VirementsAppServiceTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly FauxRepository _comptes = new(new Compte(Alice, "Alice", 100m), new Compte(Bob, "Bob", 0m));
    private readonly FauxUnitOfWork _unitOfWork = new();

    private VirementsAppService Service => new(_comptes, _unitOfWork, new ServiceVirement());

    [Fact]
    public async Task Un_virement_reussi_est_enregistre_une_seule_fois()
    {
        var resultat = await Service.Virer(Alice, Bob, 30m);

        Assert.Equal(70m, resultat.Source.Solde);
        Assert.Equal(30m, resultat.Cible.Solde);
        Assert.Equal(1, _unitOfWork.NombreEnregistrements);
    }

    [Fact]
    public async Task Un_virement_refuse_n_est_pas_enregistre()
    {
        await Assert.ThrowsAsync<RegleMetierException>(() => Service.Virer(Alice, Bob, 1_000m));

        Assert.Equal(0, _unitOfWork.NombreEnregistrements);
    }

    [Fact]
    public async Task Un_compte_inconnu_leve_CompteIntrouvable()
    {
        await Assert.ThrowsAsync<CompteIntrouvableException>(() => Service.Virer(Alice, Guid.NewGuid(), 10m));
    }

    private class FauxRepository(params Compte[] comptes) : IComptesRepository
    {
        private readonly List<Compte> _comptes = [.. comptes];
        public Task<Compte?> ParId(Guid id) => Task.FromResult(_comptes.FirstOrDefault(c => c.Id == id));
        public Task Ajouter(Compte compte) { _comptes.Add(compte); return Task.CompletedTask; }
        public Task<bool> EstVide() => Task.FromResult(_comptes.Count == 0);
    }

    private class FauxUnitOfWork : IUnitOfWork
    {
        public int NombreEnregistrements { get; private set; }
        public Task Enregistrer() { NombreEnregistrements++; return Task.CompletedTask; }
    }
}
