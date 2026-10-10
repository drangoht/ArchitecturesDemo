using Cqrs.Ecriture;
using Cqrs.Ecriture.Commandes;
using Cqrs.Ecriture.Domaine;
using Cqrs.Evenements;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Tests;

public class EcritureTests
{
    private static readonly DateTime Maintenant = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    // ─── L'agrégat : on vérifie les événements qu'il produit ───

    [Fact]
    public void Ouvrir_un_compte_produit_CompteOuvert()
    {
        var id = Guid.NewGuid();

        var compte = Compte.Ouvrir(id, "Alice", 100m, Maintenant);

        Assert.Equal([new CompteOuvert(id, "Alice", 100m, Maintenant)], compte.Evenements);
    }

    [Fact]
    public void Un_retrait_produit_ArgentRetire_avec_le_nouveau_solde()
    {
        var compte = Compte.Ouvrir(Guid.NewGuid(), "Alice", 100m, Maintenant);
        compte.ExtraireEvenements();

        compte.Retirer(30m, Maintenant);

        Assert.Equal(new ArgentRetire(compte.Id, 30m, 70m, Maintenant), Assert.Single(compte.Evenements));
    }

    [Fact]
    public void Un_retrait_refuse_ne_produit_aucun_evenement()
    {
        var compte = Compte.Ouvrir(Guid.NewGuid(), "Alice", 100m, Maintenant);
        compte.ExtraireEvenements();

        Assert.Throws<RegleMetierException>(() => compte.Retirer(500m, Maintenant));
        Assert.Empty(compte.Evenements);
    }

    // ─── Le pipeline de commandes ───

    private static IServiceProvider CoteEcriture()
    {
        var nomBase = Guid.NewGuid().ToString();
        return new ServiceCollection().AddLogging().AddCoteEcriture(_ => nomBase).BuildServiceProvider();
    }

    [Fact]
    public async Task Une_commande_invalide_est_rejetee_avant_d_atteindre_le_gestionnaire()
    {
        var services = CoteEcriture();
        var bus = services.GetRequiredService<BusCommandes>();

        var erreur = await Assert.ThrowsAsync<CommandeInvalideException>(() =>
            bus.Envoyer(new OuvrirCompte(Guid.NewGuid(), " ", 1m)));

        Assert.Equal(2, erreur.Erreurs.Count);
        Assert.Equal(0, await services.GetRequiredService<IFluxEvenements>().DerniereSequence());
    }

    [Fact]
    public async Task Les_commandes_alimentent_le_journal_d_evenements_dans_l_ordre()
    {
        var services = CoteEcriture();
        var bus = services.GetRequiredService<BusCommandes>();
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid();

        await bus.Envoyer(new OuvrirCompte(alice, "Alice", 100m));
        await bus.Envoyer(new OuvrirCompte(bob, "Bob", 50m));
        await bus.Envoyer(new EffectuerVirement(Guid.NewGuid(), alice, bob, 40m));

        var flux = await services.GetRequiredService<IFluxEvenements>().LireApres(0, 10);
        Assert.Equal([1L, 2L, 3L], flux.Select(e => e.Sequence));
        var virement = Assert.IsType<VirementEffectue>(flux[2].Evenement);
        Assert.Equal((60m, 90m), (virement.SoldeSource, virement.SoldeCible));
    }
}
