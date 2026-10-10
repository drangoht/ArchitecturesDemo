using Fonctionnel.Coeur;

namespace Fonctionnel.Tests;

// Tester une fonction pure : des valeurs en entrée, une valeur attendue en sortie. Rien d'autre.
public class ReglesTests
{
    private static readonly Compte Alice = new(Guid.NewGuid(), "Alice", 1000m);
    private static readonly Compte Bob = new(Guid.NewGuid(), "Bob", 0m);

    [Theory]
    [InlineData(1000, 30, 970)]
    [InlineData(1000, 1000, 0)]
    public void Un_retrait_possible_est_accepte(int solde, int montant, int soldeAttendu)
    {
        var decision = Regles.Retirer(Alice with { Solde = solde }, montant);

        var accepte = Assert.IsType<DecisionRetrait.Accepte>(decision);
        Assert.Equal(soldeAttendu, accepte.CompteMisAJour.Solde);
    }

    [Theory]
    [InlineData(0, "Le montant doit être strictement positif.")]
    [InlineData(-5, "Le montant doit être strictement positif.")]
    [InlineData(1001, "Solde insuffisant.")]
    public void Un_retrait_impossible_est_refuse(int montant, string raison)
    {
        var decision = Regles.Retirer(Alice, montant);

        Assert.Equal(new DecisionRetrait.Refuse(raison), decision);
    }

    [Fact]
    public void Une_decision_ne_modifie_jamais_le_compte_d_origine()
    {
        Regles.Retirer(Alice, 100m);

        Assert.Equal(1000m, Alice.Solde);
    }

    [Fact]
    public void Un_virement_sous_le_plafond_deplace_l_argent()
    {
        var decision = Regles.Virer(Alice, Bob, 300m, dejaVireAujourdhui: 0m);

        Assert.Equal(new DecisionVirement.Accepte(Alice with { Solde = 700m }, Bob with { Solde = 300m }, 300m), decision);
    }

    [Fact]
    public void Le_plafond_journalier_tient_compte_de_ce_qui_a_deja_ete_vire()
    {
        // Le « temps » est une simple donnée : pas besoin de simuler une horloge.
        var decision = Regles.Virer(Alice with { Solde = 10_000m }, Bob, 600m, dejaVireAujourdhui: 1_500m);

        Assert.IsType<DecisionVirement.Refuse>(decision);
    }

    [Fact]
    public void Un_virement_reprend_les_refus_de_la_regle_de_retrait()
    {
        var decision = Regles.Virer(Alice, Bob, 1_500m, dejaVireAujourdhui: 0m);

        Assert.Equal(new DecisionVirement.Refuse("Solde insuffisant."), decision);
    }
}
