using Hexa.Adapters.Persistance;
using Hexa.Hexagone.Application;
using Hexa.Hexagone.Domaine;

namespace Hexa.Tests.Hexagone;

// L'hexagone est testé en branchant des adapters de test sur ses ports :
// ni serveur web, ni base de données.
public class CasUtilisationTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly ComptesEnMemoire _comptes = new([new Compte(Alice, "Alice", 100m), new Compte(Bob, "Bob", 20m)]);
    private readonly NotificationsEspion _notifications = new();

    [Fact]
    public async Task Un_retrait_valide_debite_le_compte_et_notifie()
    {
        await new RetirerArgent(_comptes, _notifications).Executer(Alice, 30m);

        Assert.Equal(70m, (await _comptes.Charger(Alice))!.Solde);
        Assert.Equal([$"retrait 30 {Alice}"], _notifications.Envoyees);
    }

    [Fact]
    public async Task Un_retrait_superieur_au_solde_est_refuse_sans_effet()
    {
        var retrait = () => new RetirerArgent(_comptes, _notifications).Executer(Alice, 500m);

        var erreur = await Assert.ThrowsAsync<RegleMetierException>(retrait);
        Assert.Equal("Solde insuffisant.", erreur.Message);
        Assert.Equal(100m, (await _comptes.Charger(Alice))!.Solde);
        Assert.Empty(_notifications.Envoyees);
    }

    [Fact]
    public async Task Un_compte_inconnu_leve_CompteIntrouvable()
    {
        var consultation = () => new ConsulterCompte(_comptes).Executer(Guid.NewGuid());

        await Assert.ThrowsAsync<CompteIntrouvableException>(consultation);
    }

    [Fact]
    public async Task Un_virement_debite_la_source_et_credite_la_cible()
    {
        await new EffectuerVirement(_comptes, _notifications).Executer(Alice, Bob, 40m);

        Assert.Equal(60m, (await _comptes.Charger(Alice))!.Solde);
        Assert.Equal(60m, (await _comptes.Charger(Bob))!.Solde);
        Assert.Single(_notifications.Envoyees);
    }

    [Fact]
    public async Task Un_virement_vers_le_meme_compte_est_refuse()
    {
        var virement = () => new EffectuerVirement(_comptes, _notifications).Executer(Alice, Alice, 10m);

        await Assert.ThrowsAsync<RegleMetierException>(virement);
    }
}
