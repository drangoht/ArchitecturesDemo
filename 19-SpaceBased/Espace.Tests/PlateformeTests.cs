using Espace.Banque;
using Espace.Grille;

namespace Espace.Tests;

/// <summary>L'intergiciel et le métier ensemble, sans HTTP. Latences nulles ; la réplication est retenue à la main quand il le faut.</summary>
public class PlateformeTests : IAsyncLifetime
{
    private static readonly Guid Alice = DonneesDeDemo.Alice, Bob = DonneesDeDemo.Bob;

    private Plateforme _p = null!;

    public async Task InitializeAsync() => _p = await Nouvelle(new OptionsPlateforme());

    public async Task DisposeAsync() => await _p.DisposeAsync();

    private static async Task<Plateforme> Nouvelle(OptionsPlateforme options)
    {
        var plateforme = new Plateforme(options, Traitement.Traiter);
        await plateforme.Demarrer(DonneesDeDemo.Comptes());
        return plateforme;
    }

    private async Task<(object Reponse, string Unite)> Envoyer(IRequete requete) => await _p.Envoyer(requete);

    private async Task<decimal> Solde(Guid compte) => ((CompteDto)(await Envoyer(new ConsulterCompte(compte))).Reponse).Solde;

    private IEnumerable<decimal> SoldesDesRepliques(Guid compte) =>
        _p.Donnees.Repliques().Select(r => ((Compte)r.Lire(compte.ToString())!.Valeur).Solde);

    private decimal SoldeEnBase(Guid compte) => ((Compte)_p.Base.Lire(compte.ToString())!.Valeur).Solde;

    [Fact]
    public async Task Une_ecriture_est_repliquee_dans_toutes_les_unites_puis_dans_la_base()
    {
        await Envoyer(new Retirer(Alice, 100m));

        await _p.AttendreStabilisation();

        Assert.Equal([900m, 900m], SoldesDesRepliques(Alice));
        Assert.Equal(900m, SoldeEnBase(Alice));
    }

    [Fact]
    public async Task Les_requetes_n_attendent_jamais_la_base()
    {
        _p.Base.Disponible = false;  // la base tombe…

        for (var i = 0; i < 10; i++)
            Assert.IsType<CompteDto>((await Envoyer(new Retirer(Alice, 10m))).Reponse);  // … les requêtes continuent

        Assert.Equal(10, _p.Pompe.EnAttente);  // la pompe garde les écritures
        Assert.Equal(1_000m, SoldeEnBase(Alice));
        Assert.Equal(1, _p.Base.Lectures);  // la seule lecture : le démarrage à froid initial

        _p.Base.Disponible = true;
        await _p.Pompe.AttendreVidage();
        Assert.Equal(900m, SoldeEnBase(Alice));  // et la base rattrape son retard
    }

    [Fact]
    public async Task En_affinite_une_meme_cle_va_toujours_a_la_meme_unite()
    {
        await _p.Deploiement.Demarrer();
        await _p.Deploiement.Demarrer();

        var unites = new HashSet<string>();
        for (var i = 0; i < 20; i++)
            unites.Add((await Envoyer(new ConsulterCompte(Alice))).Unite);

        Assert.Single(unites);
    }

    [Fact]
    public async Task Au_tourniquet_deux_unites_qui_ecrivent_la_meme_donnee_entrent_en_collision()
    {
        _p.Messages.Mode = ModeRoutage.Tourniquet;
        _p.Donnees.Suspendre();  // chaque unité écrit avant d'avoir vu l'écriture de l'autre

        var premier = await Envoyer(new Retirer(Alice, 100m));  // u2
        var second = await Envoyer(new Retirer(Alice, 50m));    // u1
        Assert.NotEqual(premier.Unite, second.Unite);
        Assert.Equal(900m, ((CompteDto)premier.Reponse).Solde);  // les deux clients reçoivent « OK »…
        Assert.Equal(950m, ((CompteDto)second.Reponse).Solde);

        _p.Donnees.Reprendre();
        await _p.AttendreStabilisation();

        // … mais l'écriture de u2 (la plus grande version) a écrasé celle de u1, partout : 100 € retirés au lieu de 150.
        Assert.Equal([900m, 900m], SoldesDesRepliques(Alice));
        Assert.Equal(900m, SoldeEnBase(Alice));  // la base a reçu l'écriture perdante en dernier, et l'a ignorée
        Assert.Equal(_p.Donnees.Repliques()[0].Lire(Alice.ToString())!.Version, _p.Base.Lire(Alice.ToString())!.Version);
        Assert.Equal(1, _p.Donnees.MisesAJourPerdues);
        Assert.Contains(_p.Journal(), e => e.Contains("COLLISION"));
    }

    [Fact]
    public async Task En_affinite_des_retraits_simultanes_ne_perdent_rien()
    {
        await using var p = await Nouvelle(new OptionsPlateforme { UnitesAuDemarrage = 3, LatenceReplicationMs = 20 });

        var reponses = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => p.Envoyer(new Retirer(Alice, 3m)))));
        await p.AttendreStabilisation();

        Assert.All(reponses, r => Assert.IsType<CompteDto>(r.Reponse));
        Assert.Single(reponses.Select(r => r.Unite).Distinct());
        Assert.All(p.Donnees.Repliques(), r => Assert.Equal(400m, ((Compte)r.Lire(Alice.ToString())!.Valeur).Solde));
        Assert.Equal(400m, ((Compte)p.Base.Lire(Alice.ToString())!.Valeur).Solde);
        Assert.Equal(0, p.Donnees.MisesAJourPerdues);
    }

    [Fact]
    public async Task En_affinite_un_changement_d_unites_ouvre_une_fenetre_de_collision()
    {
        var (_, ancienne) = await Envoyer(new Retirer(Alice, 100m));
        await _p.Donnees.AttendreReplication();
        _p.Donnees.Suspendre();
        await Envoyer(new Retirer(Alice, 100m));  // écrit par le propriétaire d'Alice, pas encore répliqué

        await _p.Deploiement.Arreter(_p.Messages.Unites().Single(u => u.Id == ancienne));  // Alice change de propriétaire
        var (reponse, nouvelle) = await Envoyer(new Retirer(Alice, 50m));
        Assert.NotEqual(ancienne, nouvelle);
        Assert.Equal(850m, ((CompteDto)reponse).Solde);  // la nouvelle unité ne connaît pas encore le 2e retrait

        _p.Donnees.Reprendre();
        await _p.AttendreStabilisation();
        // 250 € de retraits acceptés ; selon l'écriture qui gagne, 200 € ou 150 € débités, jamais 250 €.
        Assert.Equal(1, _p.Donnees.MisesAJourPerdues);
        Assert.Contains(await Solde(Alice), new[] { 800m, 850m });
    }

    [Fact]
    public async Task La_charge_ajoute_des_unites_et_le_calme_les_retire()
    {
        await using var p = await Nouvelle(new OptionsPlateforme { UnitesAuDemarrage = 1, CapaciteParUnite = 2, UnitesMax = 3 });
        p.Simulation.Fermer();  // les requêtes restent en cours

        var requetes = Enumerable.Range(0, 4).Select(i => p.Envoyer(new OuvrirCompte(Guid.NewGuid(), $"Client {i}", 10m))).ToList();
        while (p.Messages.Unites()[0].EnCours + p.Messages.Unites()[0].EnAttente < 4)
            await Task.Delay(5);

        Assert.Equal("charge 200 % : u2 ajoutée", await p.Deploiement.Evaluer());
        Assert.Equal("charge 100 % : u3 ajoutée", await p.Deploiement.Evaluer());
        Assert.Null(await p.Deploiement.Evaluer());  // plafond de 3 unités atteint
        Assert.All(p.Messages.Unites(), u => Assert.Equal(2, u.Replique.Nombre));  // copies complètes de la grille

        p.Simulation.Ouvrir();
        await Task.WhenAll(requetes);

        Assert.StartsWith("charge 0 % :", await p.Deploiement.Evaluer());
        Assert.StartsWith("charge 0 % :", await p.Deploiement.Evaluer());
        Assert.Null(await p.Deploiement.Evaluer());  // minimum : 1 unité
        Assert.Single(p.Messages.Unites());
    }

    [Fact]
    public async Task Une_nouvelle_unite_recoit_une_copie_de_la_grille_sans_lire_la_base()
    {
        await Envoyer(new Retirer(Alice, 100m));
        await Envoyer(new OuvrirCompte(Guid.NewGuid(), "Zoé", 50m));
        await _p.Donnees.AttendreReplication();

        var nouvelle = await _p.Deploiement.Demarrer();

        var reference = _p.Messages.Unites()[0].Replique.Toutes().OrderBy(e => e.Cle).Select(e => (e.Cle, e.Version));
        Assert.Equal(reference, nouvelle.Replique.Toutes().OrderBy(e => e.Cle).Select(e => (e.Cle, e.Version)));
        Assert.Equal(1, _p.Base.Lectures);
    }

    [Fact]
    public async Task La_panne_d_une_unite_ne_perd_aucune_donnee()
    {
        var (_, unite) = await Envoyer(new Retirer(Alice, 100m));
        await _p.Donnees.AttendreReplication();

        _p.Deploiement.Tuer(_p.Messages.Unites().Single(u => u.Id == unite));

        var (reponse, autre) = await Envoyer(new ConsulterCompte(Alice));
        Assert.NotEqual(unite, autre);
        Assert.Equal(900m, ((CompteDto)reponse).Solde);  // la donnée était aussi dans l'autre réplique
    }

    [Fact]
    public async Task Les_requetes_d_une_unite_tombee_sont_confiees_a_une_autre()
    {
        var unite = _p.Messages.Unites().Single(u => u.Id == (_p.Envoyer(new ConsulterCompte(Alice)).Result.Unite));
        _p.Simulation.Fermer();
        var enCours = Envoyer(new Retirer(Alice, 100m));
        var enAttente = Envoyer(new Retirer(Alice, 100m));
        while (unite.EnCours + unite.EnAttente < 2)
            await Task.Delay(5);

        _p.Deploiement.Tuer(unite);
        _p.Simulation.Ouvrir();

        Assert.All(await Task.WhenAll(enCours, enAttente), r => Assert.NotEqual(unite.Id, r.Unite));
        Assert.Equal(800m, await Solde(Alice));
    }

    [Fact]
    public async Task Apres_une_panne_totale_le_demarrage_a_froid_recharge_la_grille_depuis_la_base()
    {
        await Envoyer(new Retirer(Alice, 100m));
        await _p.AttendreStabilisation();

        foreach (var unite in _p.Messages.Unites())
            _p.Deploiement.Tuer(unite);
        Assert.Empty(_p.Messages.Unites());

        Assert.Equal(900m, await Solde(Alice));
        Assert.Equal(2, _p.Deploiement.DemarragesAFroid);  // celui du lancement, et celui-ci
        Assert.Equal(2, _p.Base.Lectures);
    }

    [Fact]
    public async Task Le_demarrage_a_froid_attend_que_la_pompe_ait_tout_ecrit()
    {
        _p.Base.Disponible = false;
        await Envoyer(new Retirer(Alice, 100m));  // écrit dans la grille, pas encore dans la base
        await _p.Donnees.AttendreReplication();
        foreach (var unite in _p.Messages.Unites())
            _p.Deploiement.Tuer(unite);

        await Assert.ThrowsAsync<BaseIndisponibleException>(() => Envoyer(new ConsulterCompte(Alice)));  // ni unité, ni base

        _p.Base.Disponible = true;
        // Sans attendre la pompe, la nouvelle grille repartirait de 1 000 € : le retrait serait perdu.
        Assert.Equal(900m, await Solde(Alice));
    }

    [Fact]
    public async Task Un_virement_debite_dans_l_unite_de_la_source_et_credite_dans_celle_de_la_cible()
    {
        while (Proprietaire(Alice) == Proprietaire(Bob))
            await _p.Deploiement.Demarrer();

        var (reponse, unite) = await Envoyer(new Virer(Alice, Bob, 100m));
        await _p.AttendreStabilisation();

        Assert.Equal(Proprietaire(Alice), unite);
        Assert.Equal(900m, ((VirementDto)reponse).SoldeSource);
        Assert.Equal(600m, await Solde(Bob));
        Assert.Equal(0, _p.Donnees.MisesAJourPerdues);
    }

    [Fact]
    public async Task Des_virements_croises_entre_deux_unites_pleines_ne_se_bloquent_pas()
    {
        await using var p = await Nouvelle(new OptionsPlateforme { CapaciteParUnite = 1 });
        while (p.Envoyer(new ConsulterCompte(Alice)).Result.Unite == p.Envoyer(new ConsulterCompte(Bob)).Result.Unite)
            await p.Deploiement.Demarrer();
        p.Simulation.Fermer();

        // Chaque unité occupe son unique place avec un virement qui demandera un crédit à l'autre unité.
        var virements = Task.WhenAll(p.Envoyer(new Virer(Alice, Bob, 10m)), p.Envoyer(new Virer(Bob, Alice, 10m)));
        while (p.Messages.Unites().Sum(u => u.EnCours) < 2)
            await Task.Delay(5);
        p.Simulation.Ouvrir();

        // Si les crédits attendaient une place, chaque unité attendrait l'autre pour toujours.
        Assert.All(await virements.WaitAsync(TimeSpan.FromSeconds(5)), r => Assert.IsType<VirementDto>(r.Reponse));
    }

    [Fact]
    public async Task Un_credit_refuse_par_l_unite_de_la_cible_est_compense()
    {
        _p.Messages.Mode = ModeRoutage.Tourniquet;
        _p.Donnees.Suspendre();  // le nouveau compte n'existe que dans la réplique de l'unité qui l'a ouvert
        var zoe = Guid.NewGuid();
        await Envoyer(new OuvrirCompte(zoe, "Zoé", 0m));

        var refus = new List<Refus>();
        for (var i = 0; i < 2; i++)  // l'un des deux virements passe par l'unité qui connaît Zoé, et demande le crédit à l'autre
            refus.Add(Assert.IsType<Refus>((await Envoyer(new Virer(Alice, zoe, 100m))).Reponse));
        _p.Donnees.Reprendre();
        await _p.AttendreStabilisation();

        Assert.All(refus, r => Assert.True(r.Introuvable));
        Assert.Equal(1_000m, await Solde(Alice));
        var operations = ((Compte)_p.Messages.Unites()[0].Replique.Lire(Alice.ToString())!.Valeur).Operations;
        Assert.Equal(["Dépôt initial", "Virement émis", "Annulation du virement"], operations.Select(o => o.Libelle));
    }

    [Fact]
    public async Task Les_refus_metier_ne_modifient_rien()
    {
        Assert.IsType<Refus>((await Envoyer(new Retirer(Alice, 5_000m))).Reponse);
        Assert.IsType<Refus>((await Envoyer(new Deposer(Alice, -5m))).Reponse);
        Assert.IsType<Refus>((await Envoyer(new OuvrirCompte(Alice, "Alice", 0m))).Reponse);
        Assert.True(((Refus)(await Envoyer(new Virer(Alice, Guid.NewGuid(), 10m))).Reponse).Introuvable);
        await _p.AttendreStabilisation();

        Assert.Equal(0, _p.Base.Ecritures);
        Assert.Equal(1_000m, await Solde(Alice));
    }

    private string Proprietaire(Guid compte) => _p.Envoyer(new ConsulterCompte(compte)).Result.Unite;
}
