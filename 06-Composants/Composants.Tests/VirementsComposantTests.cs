using Composants.Comptes;
using Composants.Virements;
using Microsoft.Extensions.DependencyInjection;

namespace Composants.Tests;

public class VirementsComposantTests
{
    [Fact]
    public async Task Un_virement_passe_par_le_composant_Comptes_et_est_historise()
    {
        var services = new ServiceCollection()
            .AddComposantComptes(Guid.NewGuid().ToString())
            .AddComposantVirements(Guid.NewGuid().ToString())
            .BuildServiceProvider();
        var comptes = services.GetRequiredService<IComptesComposant>();
        var virements = services.GetRequiredService<IVirementsComposant>();
        var alice = await comptes.Ouvrir("Alice", 100m);
        var bob = await comptes.Ouvrir("Bob", 0m);

        var resultat = await virements.Virer(alice.Id, bob.Id, 40m);

        Assert.True(resultat.Succes);
        Assert.Equal(60m, (await comptes.Consulter(alice.Id))!.Solde);
        Assert.Equal(40m, (await comptes.Consulter(bob.Id))!.Solde);
        Assert.Single(await virements.Historique(bob.Id));
    }

    [Fact]
    public async Task Si_le_depot_echoue_le_retrait_est_compense()
    {
        // Les composants ne se connaissent que par interface : on peut remplacer Comptes par un faux.
        var faux = new FauxComptes();
        var virements = new ServiceCollection()
            .AddSingleton<IComptesComposant>(faux)
            .AddComposantVirements(Guid.NewGuid().ToString())
            .BuildServiceProvider()
            .GetRequiredService<IVirementsComposant>();

        var resultat = await virements.Virer(FauxComptes.Source, FauxComptes.Cible, 40m);

        Assert.False(resultat.Succes);
        Assert.Equal(["retrait source 40", "dépôt cible 40 (refusé)", "dépôt source 40"], faux.Journal);
        Assert.Empty(await virements.Historique(FauxComptes.Source));
    }

    private class FauxComptes : IComptesComposant
    {
        public static readonly Guid Source = Guid.NewGuid();
        public static readonly Guid Cible = Guid.NewGuid();

        public List<string> Journal { get; } = [];

        public Task<CompteDto?> Consulter(Guid compteId) =>
            Task.FromResult<CompteDto?>(new CompteDto(compteId, "?", 100m));

        public Task<Resultat> Retirer(Guid compteId, decimal montant)
        {
            Journal.Add($"retrait {Nom(compteId)} {montant}");
            return Task.FromResult(Resultat.Ok());
        }

        public Task<Resultat> Deposer(Guid compteId, decimal montant)
        {
            if (compteId == Cible)
            {
                Journal.Add($"dépôt cible {montant} (refusé)");
                return Task.FromResult(Resultat.Refuse("Compte cible bloqué."));
            }
            Journal.Add($"dépôt {Nom(compteId)} {montant}");
            return Task.FromResult(Resultat.Ok());
        }

        public Task<CompteDto> Ouvrir(string titulaire, decimal depotInitial, Guid? id = null) =>
            throw new NotSupportedException();

        private static string Nom(Guid id) => id == Source ? "source" : "cible";
    }
}
