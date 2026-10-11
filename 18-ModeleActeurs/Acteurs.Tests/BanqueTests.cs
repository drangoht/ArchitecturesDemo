using Acteurs.Banque;
using Acteurs.Noyau;

namespace Acteurs.Tests;

/// <summary>Le métier sous forme d'acteurs, sans HTTP : concurrence, pannes, reprise, passivation.</summary>
public class BanqueTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid();

    private readonly HorlogeManuelle _horloge = new();
    private readonly MagasinEtats _magasin = new();
    private readonly Pieges _pieges = new();
    private readonly SystemeActeurs _systeme;

    public BanqueTests()
    {
        _systeme = new SystemeActeurs(_horloge).InstallerBanque(_magasin, _pieges, _horloge);
        _systeme.Compte(Alice).Demander(new OuvrirCompte("Alice", 1_000m)).Wait();
        _systeme.Compte(Bob).Demander(new OuvrirCompte("Bob", 500m)).Wait();
    }

    private async Task<decimal> Solde(Guid compte) =>
        ((CompteDto)(await _systeme.Compte(compte).Demander(new ConsulterCompte()))!).Solde;

    private async Task<List<OperationDto>> Operations(Guid compte) =>
        (List<OperationDto>)(await _systeme.Compte(compte).Demander(new ListerOperations()))!;

    private Task<object?> Virer(string id, Guid source, Guid cible, decimal montant) =>
        _systeme.Virement(id).Demander(new ExecuterVirement(source, cible, montant));

    [Fact]
    public async Task Mille_retraits_simultanes_ne_rendent_jamais_le_solde_negatif()
    {
        var compte = _systeme.Compte(Alice);

        var reponses = await Task.WhenAll(Enumerable.Range(0, 1_000).Select(_ => Task.Run(() => compte.Demander(new Retirer(3m)))));

        var acceptes = reponses.OfType<CompteDto>().ToList();
        Assert.Equal(333, acceptes.Count);            // 333 × 3 € = 999 €, le 334e ne passe plus
        Assert.Equal(667, reponses.OfType<Refus>().Count());
        Assert.Equal(1m, await Solde(Alice));
        Assert.All(acceptes, c => Assert.True(c.Solde >= 0));
        Assert.Equal(334, (await Operations(Alice)).Count);  // dépôt initial + 333 retraits, aucun en double
    }

    [Fact]
    public async Task Des_virements_croises_simultanes_ne_s_interbloquent_pas()
    {
        var virements = Enumerable.Range(0, 200).SelectMany(i => new[]
        {
            Task.Run(() => Virer($"ab-{i}", Alice, Bob, 10m)),
            Task.Run(() => Virer($"ba-{i}", Bob, Alice, 10m))
        });

        var reponses = await Task.WhenAll(virements).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(reponses, r => Assert.Contains(((VirementDto)r!).Statut, new[] { StatutVirement.Termine, StatutVirement.Refuse }));
        Assert.Equal(1_500m, await Solde(Alice) + await Solde(Bob));  // l'argent ne se crée ni ne se perd
    }

    [Fact]
    public async Task Une_panne_avant_la_sauvegarde_annule_le_retrait_en_cours()
    {
        _pieges.Armer(new Piege($"compte/{Alice}", Etape.AvantSauvegarde));

        await Assert.ThrowsAsync<ActeurEnPanneException>(() => _systeme.Compte(Alice).Demander(new Retirer(100m)));

        // L'incarnation qui avait déjà retiré 100 € en mémoire a été jetée ; la nouvelle a relu le stockage.
        Assert.Equal(1_000m, await Solde(Alice));
        Assert.Single(await Operations(Alice));
        Assert.Equal(1, _systeme.Etat().Redemarrages);
        Assert.Equal(900m, ((CompteDto)(await _systeme.Compte(Alice).Demander(new Retirer(100m)))!).Solde);  // et le compte fonctionne
    }

    [Fact]
    public async Task Un_virement_interrompu_apres_le_debit_se_termine_quand_on_le_rejoue()
    {
        _pieges.Armer(new Piege("virement/v1", Etape.ApresDebit));

        await Assert.ThrowsAsync<ActeurEnPanneException>(() => Virer("v1", Alice, Bob, 100m));
        Assert.Equal((900m, 500m), (await Solde(Alice), await Solde(Bob)));  // l'argent est « en vol »
        Assert.Equal(StatutVirement.DebitFait, ((VirementDto)(await _systeme.Virement("v1").Demander(new ConsulterVirement()))!).Statut);

        var rejoue = (VirementDto)(await Virer("v1", Alice, Bob, 100m))!;

        Assert.Equal(StatutVirement.Termine, rejoue.Statut);
        Assert.Equal((900m, 600m), (await Solde(Alice), await Solde(Bob)));  // débité une seule fois
    }

    [Fact]
    public async Task La_panne_d_un_compte_remonte_au_virement_qui_peut_etre_rejoue()
    {
        _pieges.Armer(new Piege($"compte/{Bob}", Etape.Reception));

        var panne = await Assert.ThrowsAsync<ActeurEnPanneException>(() => Virer("v2", Alice, Bob, 100m));

        // Le compte a planté, donc la demande du virement a échoué, donc le virement a planté à son tour.
        Assert.Equal("virement/v2", panne.Chemin);
        Assert.Equal(StatutVirement.Termine, ((VirementDto)(await Virer("v2", Alice, Bob, 100m))!).Statut);
        Assert.Equal((900m, 600m), (await Solde(Alice), await Solde(Bob)));
        Assert.Equal(2, _systeme.Etat().Redemarrages);  // le compte de Bob, puis le virement
    }

    [Fact]
    public async Task Un_debit_rejoue_avec_la_meme_cle_n_est_applique_qu_une_fois()
    {
        var compte = _systeme.Compte(Alice);

        await compte.Demander(new Debiter("cle-1", 100m, "Virement"));
        await compte.Demander(new Debiter("cle-1", 100m, "Virement"));

        Assert.Equal(900m, await Solde(Alice));
    }

    [Fact]
    public async Task Un_virement_vers_un_compte_inexistant_est_compense()
    {
        var virement = (VirementDto)(await Virer("v3", Alice, Guid.NewGuid(), 100m))!;

        Assert.Equal(StatutVirement.Annule, virement.Statut);
        Assert.Contains("introuvable", virement.Motif);
        Assert.Equal(1_000m, await Solde(Alice));
        Assert.Equal([1_000m, -100m, 100m], (await Operations(Alice)).Select(o => o.Montant));  // débit puis annulation, tracés
    }

    [Fact]
    public async Task Rejouer_un_virement_termine_renvoie_le_meme_resultat_sans_effet()
    {
        var premier = await Virer("v4", Alice, Bob, 100m);
        var second = await Virer("v4", Alice, Bob, 100m);

        Assert.Equal(premier, second);
        Assert.Equal(900m, await Solde(Alice));
        Assert.IsType<Refus>(await Virer("v4", Alice, Bob, 999m));  // même identifiant, autre virement : refusé
    }

    [Fact]
    public async Task Rejouer_un_virement_refuse_ne_credite_jamais_la_cible()
    {
        Assert.Equal(StatutVirement.Refuse, ((VirementDto)(await Virer("v6", Bob, Alice, 600m))!).Statut);  // Bob n'a que 500 €
        await _systeme.Compte(Bob).Demander(new Deposer(200m));

        // Le refus est définitif : rejouer ne retente pas le débit, et surtout ne crédite pas Alice sans avoir débité Bob.
        Assert.Equal(StatutVirement.Refuse, ((VirementDto)(await Virer("v6", Bob, Alice, 600m))!).Statut);
        Assert.Equal((1_000m, 700m), (await Solde(Alice), await Solde(Bob)));
    }

    [Fact]
    public async Task Les_refus_metier_sont_des_reponses_pas_des_pannes()
    {
        Assert.IsType<Refus>(await _systeme.Compte(Alice).Demander(new Retirer(5_000m)));
        Assert.IsType<Refus>(await _systeme.Compte(Alice).Demander(new Deposer(-5m)));
        Assert.IsType<Refus>(await _systeme.Compte(Alice).Demander(new OuvrirCompte("Alice", 0m)));
        Assert.Equal(StatutVirement.Refuse, ((VirementDto)(await Virer("v5", Bob, Alice, 600m))!).Statut);
        Assert.True(((Refus)(await _systeme.Compte(Guid.NewGuid()).Demander(new ConsulterCompte()))!).Introuvable);

        Assert.Equal(0, _systeme.Etat().Redemarrages);
    }

    [Fact]
    public async Task Un_compte_passive_retrouve_son_solde_dans_le_stockage()
    {
        await _systeme.Compte(Alice).Demander(new Retirer(100m));
        _horloge.Avancer(TimeSpan.FromMinutes(5));

        Assert.Equal(2, _systeme.Passiver(TimeSpan.FromMinutes(1)));
        Assert.Equal(0, _systeme.Etat().Actifs);
        Assert.Contains($"compte/{Alice}", _magasin.Chemins());

        Assert.Equal(900m, await Solde(Alice));
        Assert.Equal(["compte/" + Alice], _systeme.Etat().Acteurs.Select(a => a.Chemin));  // seul Alice a été réveillée
    }
}
