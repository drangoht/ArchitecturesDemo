using Pipeline.Noyau;

namespace Pipeline.Tests;

/// <summary>Un filtre de test qui prend son temps (selon l'élément) et note combien de filtres travaillent en même temps.</summary>
public sealed class Lent<T>(string nom, Func<T, int> delaiMs, Occupation? occupation = null) : IFiltre<T, T>
{
    public string Nom => nom;

    public async ValueTask<Issue<T>> Traiter(T entree, CancellationToken annulation)
    {
        occupation?.Entrer();
        try
        {
            await Task.Delay(delaiMs(entree), annulation);
            return Issue<T>.Ok(entree);
        }
        finally
        {
            occupation?.Sortir();
        }
    }
}

public sealed class Occupation
{
    private int _enCours, _max;
    public int Max => Volatile.Read(ref _max);

    public void Entrer()
    {
        var n = Interlocked.Increment(ref _enCours);
        int max;
        while (n > (max = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, n, max) != max) { }
    }

    public void Sortir() => Interlocked.Decrement(ref _enCours);
}

/// <summary>Le moteur seul, avec des filtres de test : ordre, rejets, simultanéité, contre-pression, parallélisme, pannes.</summary>
public class NoyauTests
{
    private static async IAsyncEnumerable<int> Nombres(int n)
    {
        for (var i = 1; i <= n; i++)
        {
            yield return i;
            if (i % 100 == 0)
                await Task.Yield();
        }
    }

    private static FiltreFonction<int, int> Doubler() => new("Doubler", x => Issue<int>.Ok(x * 2));

    private static FiltreFonction<int, int> RejeterLesMultiplesDe(int n) =>
        new($"Multiples de {n}", x => x % n == 0 ? Issue<int>.Rejet($"multiple de {n}", x.ToString()) : Issue<int>.Ok(x));

    [Fact]
    public async Task Les_elements_traversent_les_filtres_dans_l_ordre()
    {
        var pipeline = Pipeline.Noyau.Pipeline.Depuis<int>()
            .Puis(Doubler())
            .Puis(new FiltreFonction<int, string>("Formater", x => Issue<string>.Ok($"#{x}")))
            .Construire();
        var sorties = new List<string>();

        var bilan = await pipeline.Executer(Nombres(5), sorties.Add);

        Assert.Equal(["#2", "#4", "#6", "#8", "#10"], sorties);
        Assert.Equal((StatutExecution.Termine, 5L, 5L), (bilan.Statut, bilan.Lignes, bilan.Sorties));
        Assert.Equal(["Doubler", "Formater"], pipeline.Filtres);
    }

    [Fact]
    public async Task Un_rejet_ecarte_l_element_sans_arreter_le_flux()
    {
        var pipeline = Pipeline.Noyau.Pipeline.Depuis<int>().Puis(RejeterLesMultiplesDe(3)).Puis(RejeterLesMultiplesDe(5)).Construire();
        var sorties = new List<int>();

        var bilan = await pipeline.Executer(Nombres(15), sorties.Add);

        Assert.Equal([1, 2, 4, 7, 8, 11, 13, 14], sorties);
        Assert.Equal(7, bilan.Rejets);
        Assert.Equal(5, bilan.RejetsParMotif["multiple de 3"]);
        Assert.Equal(2, bilan.RejetsParMotif["multiple de 5"]);
        Assert.Equal(new Rejet(10, "Multiples de 5", "multiple de 5", "10"), bilan.PremiersRejets.Single(r => r.Ligne == 10));
    }

    [Fact]
    public async Task Les_etages_travaillent_en_meme_temps_chacun_sur_un_element()
    {
        var occupation = new Occupation();
        var pipeline = Pipeline.Noyau.Pipeline.Depuis<int>()
            .Puis(new Lent<int>("A", _ => 10, occupation))
            .Puis(new Lent<int>("B", _ => 10, occupation))
            .Puis(new Lent<int>("C", _ => 10, occupation))
            .Construire();

        await pipeline.Executer(Nombres(20));

        Assert.Equal(3, occupation.Max);  // A traite l'élément 3 pendant que B traite le 2 et C le 1
    }

    [Fact]
    public async Task La_contre_pression_borne_ce_qui_est_en_memoire()
    {
        // Un dernier étage lent : sans tuyaux bornés, tout le flux s'accumulerait devant lui.
        var pipeline = Pipeline.Noyau.Pipeline.Depuis<int>(capaciteTuyaux: 8)
            .Puis(Doubler())
            .Puis(Doubler())
            .Puis(new Lent<int>("Lent", x => x % 1_000 == 0 ? 20 : 0))
            .Construire();

        var bilan = await pipeline.Executer(Nombres(20_000));

        Assert.Equal(20_000, bilan.Sorties);
        // 4 tuyaux de 8 (source + 3 étages), un élément dans chaque étage, un tenu par la source, un par le puits : 37 au plus.
        Assert.InRange(bilan.MaxEnTransit, 1, 4 * 8 + 3 + 2);
        Assert.True(bilan.Etages[1].AttenteSortieMs > 0, "les étages rapides ont dû attendre le lent");
    }

    [Fact]
    public async Task Un_etage_parallele_ordonne_rend_les_elements_dans_leur_ordre_d_arrivee()
    {
        var occupation = new Occupation();
        // Le premier élément est très lent : les copies suivantes finissent avant lui.
        var lent = new Lent<int>("Lent", x => x == 1 ? 400 : 1, occupation);
        var ordonne = new List<int>();
        var desordonne = new List<int>();

        await Pipeline.Noyau.Pipeline.Depuis<int>().Puis(lent, parallelisme: 4).Puis(RejeterLesMultiplesDe(3)).Construire()
            .Executer(Nombres(30), ordonne.Add);
        await Pipeline.Noyau.Pipeline.Depuis<int>().Puis(lent, parallelisme: 4, ordonne: false).Puis(RejeterLesMultiplesDe(3)).Construire()
            .Executer(Nombres(30), desordonne.Add);

        var attendu = Enumerable.Range(1, 30).Where(x => x % 3 != 0).ToList();
        Assert.Equal(attendu, ordonne);
        Assert.NotEqual(attendu, desordonne);
        Assert.Equal(attendu, desordonne.Order());  // les mêmes éléments, pas dans le même ordre
        Assert.Equal(1, desordonne.Last());
        Assert.Equal(4, occupation.Max);
    }

    [Fact]
    public async Task Une_exception_dans_un_filtre_arrete_tout_le_pipeline()
    {
        var pipeline = Pipeline.Noyau.Pipeline.Depuis<int>(capaciteTuyaux: 8)
            .Puis(new FiltreFonction<int, int>("Fragile", x => x == 500 ? throw new InvalidOperationException("boum à 500") : Issue<int>.Ok(x)))
            .Puis(Doubler())
            .Construire();

        var bilan = await pipeline.Executer(Nombres(1_000_000)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(StatutExecution.Echoue, bilan.Statut);
        Assert.Equal("boum à 500", bilan.Erreur);
        Assert.InRange(bilan.Lignes, 500, 600);  // la source s'est arrêtée peu après : elle n'a pas lu le million
    }

    [Fact]
    public async Task L_annulation_arrete_un_flux_sans_fin()
    {
        static async IAsyncEnumerable<int> SansFin()
        {
            for (var i = 0; ; i++)
            {
                yield return i;
                await Task.Yield();
            }
        }
        using var annulation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var bilan = await Pipeline.Noyau.Pipeline.Depuis<int>().Puis(Doubler()).Construire()
            .Executer(SansFin(), annulation: annulation.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(StatutExecution.Annule, bilan.Statut);
        Assert.True(bilan.Lignes > 0);
    }

    [Fact]
    public async Task Le_goulot_est_l_etage_le_plus_occupe_et_chaque_execution_a_ses_compteurs()
    {
        var pipeline = Pipeline.Noyau.Pipeline.Depuis<int>().Puis(Doubler()).Puis(new Lent<int>("Lent", _ => 2)).Puis(Doubler()).Construire();

        var premier = await pipeline.Executer(Nombres(50));
        var second = await pipeline.Executer(Nombres(20));

        Assert.Equal("Lent", premier.Goulot);
        Assert.Equal(50, premier.Etages[1].Entrees);
        Assert.Equal(20, second.Etages[1].Entrees);
    }
}
