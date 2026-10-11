using Acteurs.Noyau;

namespace Acteurs.Tests;

/// <summary>Une horloge qu'on avance à la main : passivation et quarantaine testées sans attendre.</summary>
public sealed class HorlogeManuelle : TimeProvider
{
    private DateTimeOffset _maintenant = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _maintenant;
    public void Avancer(TimeSpan duree) => _maintenant += duree;
}

/// <summary>Ce que les acteurs-jouets rapportent au test, de l'extérieur (compteurs atomiques : plusieurs acteurs y écrivent).</summary>
public sealed class Sonde
{
    public int EnCours, MaxEnCours, Demarrages, Arrets, Vivantes, MaxVivantes;
    public readonly TaskCompletionSource Porte = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Max(ref int max, int valeur)
    {
        int actuel;
        while (valeur > (actuel = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, valeur, actuel) != actuel) { }
    }
}

public sealed record Incrementer;
public sealed record Lire;
public sealed record Noter(int Valeur);
public sealed record Notes;
public sealed record Planter;
public sealed record Lent(int Ms);
public sealed record Bloquer;

/// <summary>
/// Un acteur-jouet. Son compteur est incrémenté de la façon la plus dangereuse qui soit (lire, céder le thread, écrire) :
/// sans la garantie « un message à la fois », des incréments seraient perdus. Son état n'est PAS durable.
/// </summary>
public sealed class Jouet(Sonde sonde) : Acteur
{
    private int _compteur;
    private readonly List<int> _notes = [];

    public override async Task Demarrer()
    {
        Interlocked.Increment(ref sonde.Demarrages);
        Sonde.Max(ref sonde.MaxVivantes, Interlocked.Increment(ref sonde.Vivantes));
        await Task.Delay(1);
    }

    public override async Task Arreter()
    {
        await Task.Delay(5);  // un arrêt qui prend son temps : une autre incarnation pourrait démarrer pendant ce temps
        Interlocked.Decrement(ref sonde.Vivantes);
        Interlocked.Increment(ref sonde.Arrets);
    }

    public override async Task<object?> Recevoir(object message)
    {
        Sonde.Max(ref sonde.MaxEnCours, Interlocked.Increment(ref sonde.EnCours));
        try
        {
            switch (message)
            {
                case Incrementer:
                    var lu = _compteur;
                    await Task.Yield();
                    _compteur = lu + 1;
                    return _compteur;
                case Lire:
                    return _compteur;
                case Noter n:
                    _notes.Add(n.Valeur);
                    return null;
                case Notes:
                    return _notes.ToList();
                case Lent l:
                    await Task.Delay(l.Ms);
                    return "fini";
                case Bloquer:
                    await sonde.Porte.Task;
                    return "débloqué";
                case Planter:
                    throw new InvalidOperationException("boum");
                default:
                    throw new NotSupportedException();
            }
        }
        finally
        {
            Interlocked.Decrement(ref sonde.EnCours);
        }
    }
}

public class NoyauTests
{
    private readonly HorlogeManuelle _horloge = new();
    private readonly Sonde _sonde = new();
    private readonly SystemeActeurs _systeme;

    public NoyauTests()
    {
        _systeme = new SystemeActeurs(_horloge);
        _systeme.Enregistrer("jouet", () => new Jouet(_sonde), new StrategieSupervision(2, TimeSpan.FromMinutes(1)));
    }

    private ReferenceActeur Jouet(string cle = "a") => _systeme.Acteur("jouet", cle);

    private static async Task Attendre(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task Un_acteur_traite_un_seul_message_a_la_fois()
    {
        var jouet = Jouet();

        await Task.WhenAll(Enumerable.Range(0, 1_000).Select(_ => Task.Run(() => jouet.Demander(new Incrementer()))));

        Assert.Equal(1_000, await jouet.Demander(new Lire()));  // aucun incrément perdu, sans le moindre verrou
        Assert.Equal(1, _sonde.MaxEnCours);
    }

    [Fact]
    public async Task Des_acteurs_differents_travaillent_en_parallele()
    {
        await Task.WhenAll(Jouet("a").Demander(new Lent(200)), Jouet("b").Demander(new Lent(200)));

        Assert.Equal(2, _sonde.MaxEnCours);
    }

    [Fact]
    public async Task Les_messages_d_un_expediteur_sont_traites_dans_l_ordre_d_envoi()
    {
        var jouet = Jouet();
        for (var i = 0; i < 100; i++)
            jouet.Envoyer(new Noter(i));  // « Tell » : on n'attend rien

        Assert.Equal(Enumerable.Range(0, 100), (List<int>)(await jouet.Demander(new Notes()))!);
    }

    [Fact]
    public async Task Le_premier_message_active_l_acteur_et_l_inactivite_le_passive()
    {
        var jouet = Jouet();
        Assert.Equal(0, _systeme.Etat().Actifs);  // une adresse n'active rien

        await jouet.Demander(new Incrementer());
        Assert.Equal(1, _systeme.Etat().Actifs);
        Assert.Equal(0, _systeme.Passiver(TimeSpan.FromSeconds(30)));  // trop récent

        _horloge.Avancer(TimeSpan.FromSeconds(31));
        Assert.Equal(1, _systeme.Passiver(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, _systeme.Etat().Actifs);
        await Attendre(() => _sonde.Arrets == 1);

        // Le message suivant le réveille : nouvelle incarnation, mémoire vierge (ce jouet ne stocke rien).
        Assert.Equal(0, await jouet.Demander(new Lire()));
        Assert.Equal(2, _sonde.Demarrages);
        Assert.Equal(2, _systeme.Etat().Activations);
    }

    [Fact]
    public async Task Un_acteur_occupe_n_est_jamais_passive()
    {
        var jouet = Jouet();
        var bloque = jouet.Demander(new Bloquer());
        await Attendre(() => _sonde.EnCours == 1);
        _horloge.Avancer(TimeSpan.FromHours(1));

        Assert.Equal(0, _systeme.Passiver(TimeSpan.Zero));

        _sonde.Porte.SetResult();
        Assert.Equal("débloqué", await bloque);
    }

    [Fact]
    public async Task Jamais_deux_incarnations_du_meme_acteur_en_meme_temps()
    {
        var jouet = Jouet();

        // Messages et passivations s'entrechoquent : chaque passivation lance un arrêt lent, et le message suivant
        // une réactivation. La nouvelle incarnation doit attendre que l'ancienne ait fini de s'arrêter.
        for (var tour = 0; tour < 30; tour++)
        {
            var messages = Enumerable.Range(0, 10).Select(_ => Task.Run(() => jouet.Demander(new Incrementer())));
            var passivations = Enumerable.Range(0, 5).Select(_ => Task.Run(() => _systeme.Passiver(TimeSpan.Zero)));
            await Task.WhenAll([.. messages, .. passivations]);
        }

        Assert.True(_sonde.Demarrages > 1, "le test doit provoquer des réactivations");
        Assert.Equal(1, _sonde.MaxVivantes);
    }

    [Fact]
    public async Task Une_panne_remplace_l_incarnation_sans_perdre_les_messages_suivants()
    {
        var jouet = Jouet();
        var bloque = jouet.Demander(new Bloquer());  // tout ce qui suit s'accumule dans la boîte
        var un = jouet.Demander(new Incrementer());
        var deux = jouet.Demander(new Incrementer());
        var boum = jouet.Demander(new Planter());
        var apres = jouet.Demander(new Incrementer());
        await Attendre(() => _sonde.EnCours == 1);

        _sonde.Porte.SetResult();

        Assert.Equal(1, await un);
        Assert.Equal(2, await deux);
        var panne = await Assert.ThrowsAsync<ActeurEnPanneException>(() => boum);
        Assert.Equal("jouet/a", panne.Chemin);
        // Le message suivant est traité par une incarnation NEUVE : la mémoire non stockée est repartie de zéro.
        Assert.Equal(1, await apres);
        Assert.Equal(1, _systeme.Etat().Redemarrages);
        Assert.Contains(_systeme.Journal(), e => e.Contains("jouet/a redémarré après une panne : boum"));
    }

    [Fact]
    public async Task Une_panne_ne_touche_pas_les_autres_acteurs()
    {
        await Jouet("a").Demander(new Incrementer());
        await Jouet("a").Demander(new Incrementer());

        await Assert.ThrowsAsync<ActeurEnPanneException>(() => Jouet("b").Demander(new Planter()));

        Assert.Equal(2, await Jouet("a").Demander(new Lire()));
        Assert.Equal(0, _systeme.Etat().Acteurs.Single(a => a.Chemin == "jouet/a").Redemarrages);
    }

    [Fact]
    public async Task Trop_de_pannes_arretent_l_acteur_jusqu_a_la_fin_de_la_fenetre()
    {
        var jouet = Jouet();
        for (var i = 0; i < 3; i++)  // stratégie : 2 redémarrages par minute ; la 3e panne arrête l'acteur
            await Assert.ThrowsAsync<ActeurEnPanneException>(() => jouet.Demander(new Planter()));
        await Attendre(() => _systeme.Etat().Arrets == 1);  // l'appelant est prévenu de la panne avant la décision du superviseur

        await Assert.ThrowsAsync<ActeurIndisponibleException>(() => jouet.Demander(new Lire()));
        jouet.Envoyer(new Incrementer());  // personne ne le recevra
        var etat = _systeme.Etat();
        Assert.Equal((2, 1, 1, 0), (etat.Redemarrages, etat.Arrets, etat.LettresMortes, etat.Actifs));

        _horloge.Avancer(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await jouet.Demander(new Lire()));  // quarantaine finie : réactivé à neuf
    }

    [Fact]
    public async Task Une_demande_trop_longue_expire_sans_que_le_message_soit_perdu()
    {
        var jouet = Jouet();
        _ = jouet.Demander(new Bloquer());
        var incrementEnAttente = jouet.Demander(new Incrementer());

        await Assert.ThrowsAsync<TimeoutException>(() => jouet.Demander(new Lire(), TimeSpan.FromMilliseconds(50)));

        _sonde.Porte.SetResult();
        Assert.Equal(1, await incrementEnAttente);
        Assert.Equal(1, await jouet.Demander(new Lire()));
    }

    [Fact]
    public void Un_type_d_acteur_inconnu_est_refuse_des_l_adresse() =>
        Assert.Throws<ArgumentException>(() => _systeme.Acteur("inconnu", "1"));
}
