using Composants.Comptes;
using Microsoft.Extensions.DependencyInjection;

namespace Composants.Tests;

// Le composant est testé en boîte noire, par son interface publique, avec sa vraie base (en mémoire).
public class ComptesComposantTests
{
    private readonly IComptesComposant _comptes = new ServiceCollection()
        .AddComposantComptes(nomBase: Guid.NewGuid().ToString())
        .BuildServiceProvider()
        .GetRequiredService<IComptesComposant>();

    [Fact]
    public async Task Un_compte_ouvert_est_consultable()
    {
        var ouvert = await _comptes.Ouvrir("Alice", 100m);

        Assert.Equal(ouvert, await _comptes.Consulter(ouvert.Id));
    }

    [Fact]
    public async Task Un_retrait_trop_eleve_est_refuse_et_rien_ne_change()
    {
        var alice = await _comptes.Ouvrir("Alice", 100m);

        var resultat = await _comptes.Retirer(alice.Id, 500m);

        Assert.Equal(Resultat.Refuse("Solde insuffisant."), resultat);
        Assert.Equal(100m, (await _comptes.Consulter(alice.Id))!.Solde);
    }

    [Fact]
    public async Task Un_compte_inconnu_est_signale()
    {
        var resultat = await _comptes.Retirer(Guid.NewGuid(), 10m);

        Assert.Equal(StatutResultat.Introuvable, resultat.Statut);
    }

    [Fact]
    public void L_entite_interne_reste_testable_grace_a_InternalsVisibleTo()
    {
        var compte = new Compte(Guid.NewGuid(), "Alice", 50m);

        Assert.Equal(StatutResultat.Refuse, compte.Retirer(-1m).Statut);
    }
}
