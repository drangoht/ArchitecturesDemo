using Es.Domaine;

namespace Es.Tests;

/// <summary>
/// Le style de test naturel en Event Sourcing :
/// ÉTANT DONNÉ des événements passés, QUAND on envoie une commande, ALORS on attend ces nouveaux événements.
/// </summary>
public class CompteTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTime T = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<IEvenement> Quand(IEvenement[] etantDonne, Action<Compte> commande)
    {
        var compte = Compte.Reconstituer(etantDonne);
        commande(compte);
        return compte.NouveauxEvenements;
    }

    private static readonly IEvenement[] CompteAvec100Euros = [new CompteOuvert(Id, "Alice", 100m, T)];

    [Fact]
    public void Un_retrait_possible_produit_ArgentRetire()
    {
        var alors = Quand(CompteAvec100Euros, c => c.Retirer(30m, "Courses", T));

        Assert.Equal([new ArgentRetire(Id, 30m, "Courses", T)], alors);
    }

    [Fact]
    public void Un_retrait_superieur_au_solde_est_refuse()
    {
        Assert.Throws<RegleMetierException>(() => Quand(CompteAvec100Euros, c => c.Retirer(130m, "Courses", T)));
    }

    [Fact]
    public void L_etat_se_deduit_des_evenements()
    {
        var compte = Compte.Reconstituer(
        [
            new CompteOuvert(Id, "Alice", 100m, T),
            new ArgentDepose(Id, 50m, T),
            new ArgentRetire(Id, 30m, "Courses", T),
            new VirementRecu(Id, Guid.NewGuid(), Guid.NewGuid(), 5m, T)
        ]);

        Assert.Equal((125m, 4), (compte.Solde, compte.Version));
    }

    [Fact]
    public void Un_compte_ne_peut_etre_cloture_que_si_son_solde_est_nul()
    {
        Assert.Throws<RegleMetierException>(() => Quand(CompteAvec100Euros, c => c.Cloturer(T)));

        var alors = Quand([.. CompteAvec100Euros, new ArgentRetire(Id, 100m, "Solde", T)], c => c.Cloturer(T));

        Assert.Equal([new CompteCloture(Id, T)], alors);
    }

    [Fact]
    public void Un_compte_cloture_refuse_toute_operation()
    {
        IEvenement[] etantDonne = [new CompteOuvert(Id, "Alice", 10m, T), new ArgentRetire(Id, 10m, "Solde", T), new CompteCloture(Id, T)];

        var erreur = Assert.Throws<RegleMetierException>(() => Quand(etantDonne, c => c.Deposer(20m, T)));

        Assert.Equal("Ce compte est clôturé.", erreur.Message);
    }
}
