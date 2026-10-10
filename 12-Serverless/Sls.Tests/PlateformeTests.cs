using Sls.Plateforme;

namespace Sls.Tests;

/// <summary>Le comportement du « fournisseur cloud », vérifié avec des fonctions de test.</summary>
public class PlateformeTests
{
    private static readonly OptionsPlateforme Options = new()
    {
        DemarrageAFroidMs = 30, MsAvantRecyclage = 100, InstancesMaxParFonction = 3
    };

    private static MoteurExecution Moteur(Action<Manifeste> deploiement)
    {
        var manifeste = new Manifeste();
        deploiement(manifeste);
        return new MoteurExecution(manifeste, new ServicesGeres(Options), Options);
    }

    private static RequeteHttp Requete() => new("GET", "/", new Dictionary<string, string>(), "");

    private static Manifeste Lente(Manifeste m, int dureeMs) =>
        m.Http("Lente", "GET", "/", async (_, _) => { await Task.Delay(dureeMs); return ReponseHttp.Ok(null); });

    [Fact]
    public async Task Le_premier_appel_demarre_a_froid_les_suivants_reutilisent_l_instance_chaude()
    {
        var moteur = Moteur(m => Lente(m, 1));

        var premier = await moteur.Invoquer("Lente", Requete());
        var second = await moteur.Invoquer("Lente", Requete());

        Assert.True(premier.DemarrageAFroid);
        Assert.False(second.DemarrageAFroid);
        Assert.Equal(premier.Instance, second.Instance);
    }

    [Fact]
    public async Task Des_appels_simultanes_font_monter_le_nombre_d_instances()
    {
        var moteur = Moteur(m => Lente(m, 100));

        var resultats = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => moteur.Invoquer("Lente", Requete())));

        Assert.Equal(3, resultats.Select(r => r.Instance).Distinct().Count());
        Assert.Equal(3, moteur.Statistiques().Single().PicInstances);
    }

    [Fact]
    public async Task Au_dela_du_maximum_d_instances_les_appels_sont_etrangles()
    {
        var moteur = Moteur(m => Lente(m, 200));

        var appels = Enumerable.Range(0, 4).Select(_ => moteur.Invoquer("Lente", Requete())).ToList();

        await Assert.ThrowsAsync<EtranglementException>(() => Task.WhenAll(appels));
        Assert.Equal(3, appels.Count(a => a.IsCompletedSuccessfully));
    }

    [Fact]
    public async Task Sans_trafic_les_instances_sont_recyclees_et_leur_memoire_perdue()
    {
        var moteur = Moteur(m => m.Http("Memoire", "GET", "/", (_, contexte) =>
        {
            var appels = (contexte.MemoireInstance.TryGetValue("n", out var n) ? (int)n : 0) + 1;
            contexte.MemoireInstance["n"] = appels;
            return Task.FromResult(ReponseHttp.Ok(appels));
        }));

        await moteur.Invoquer("Memoire", Requete());
        var chaud = await moteur.Invoquer("Memoire", Requete());
        await Task.Delay(150);
        moteur.Recycler();
        var apresRecyclage = await moteur.Invoquer("Memoire", Requete());

        Assert.Equal(2, ((ReponseHttp)chaud.Valeur!).Corps);
        Assert.True(apresRecyclage.DemarrageAFroid);
        Assert.Equal(1, ((ReponseHttp)apresRecyclage.Valeur!).Corps);  // la mémoire de l'ancienne instance a disparu
        Assert.Equal(1, moteur.Statistiques().Single().InstancesActives);
    }

    [Fact]
    public async Task Une_fonction_qui_depasse_son_delai_est_arretee()
    {
        var moteur = Moteur(m => m.Http("Bloquee", "GET", "/",
            async (_, _) => { await Task.Delay(2_000); return ReponseHttp.Ok(null); }, delaiSecondes: 0.1));

        await Assert.ThrowsAsync<DelaiDepasseException>(() => moteur.Invoquer("Bloquee", Requete()));

        var statistiques = moteur.Statistiques().Single();
        Assert.Equal(1, statistiques.Erreurs);
        Assert.Equal(0, statistiques.InstancesActives);  // l'instance fautive a été détruite
    }

    [Fact]
    public async Task On_paie_la_duree_multipliee_par_la_memoire_allouee()
    {
        var moteur = Moteur(m => m.Http("Grosse", "GET", "/",
            async (_, _) => { await Task.Delay(100); return ReponseHttp.Ok(null); }, memoireMo: 1024));

        await moteur.Invoquer("Grosse", Requete());

        // 1 Go pendant ~130 ms (démarrage à froid compris) ≈ 0,13 Go-s
        Assert.InRange(moteur.Statistiques().Single().GoSecondes, 0.12, 0.5);
    }
}
