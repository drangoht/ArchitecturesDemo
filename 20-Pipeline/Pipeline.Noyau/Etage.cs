using System.Diagnostics;
using System.Threading.Channels;

namespace Pipeline.Noyau;

/// <summary>Ce qu'un étage a fait, mesuré pendant l'exécution.</summary>
public sealed record EtatEtage(
    string Nom,
    int Parallelisme,
    long Entrees,
    long Sorties,
    long Rejets,
    long TraitementMs,
    long AttenteSortieMs,
    double Occupation);

/// <summary>Partagé par tous les étages d'une exécution : rejets, éléments en transit, annulation.</summary>
internal sealed class ContexteExecution(CancellationToken annulation, Action<Rejet> rejeter)
{
    private long _enTransit, _maxEnTransit;

    public CancellationToken Annulation { get; } = annulation;

    public long MaxEnTransit => Interlocked.Read(ref _maxEnTransit);

    public void Entrer()
    {
        var n = Interlocked.Increment(ref _enTransit);
        long max;
        while (n > (max = Interlocked.Read(ref _maxEnTransit)) && Interlocked.CompareExchange(ref _maxEnTransit, n, max) != max) { }
    }

    public void Sortir() => Interlocked.Decrement(ref _enTransit);

    public void Rejeter(Rejet rejet)
    {
        rejeter(rejet);
        Sortir();
    }
}

/// <summary>Un étage du pipeline : un filtre, son tuyau de sortie, et le nombre de copies du filtre qui travaillent en même temps.</summary>
internal abstract class Etage(string nom, int parallelisme, bool ordonne)
{
    protected long Entrees, Sorties, Rejets, TicksTraitement, TicksAttenteSortie;

    public string Nom { get; } = nom;
    public int Parallelisme { get; } = parallelisme;
    public bool Ordonne { get; } = ordonne;

    /// <summary>Démarre l'étage : lit le tuyau d'entrée, renvoie le tuyau de sortie (borné : c'est la contre-pression).</summary>
    public abstract (Task Travail, object Sortie) Lancer(object entree, int capacite, ContexteExecution contexte);

    /// <summary>Le même étage, compteurs à zéro (chaque exécution mesure les siens ; le filtre, lui, est partagé).</summary>
    public abstract Etage Copier();

    public EtatEtage Etat(TimeSpan duree)
    {
        var traitement = TimeSpan.FromTicks(Interlocked.Read(ref TicksTraitement));
        var occupation = duree > TimeSpan.Zero ? traitement / (duree * Parallelisme) : 0;
        return new EtatEtage(Nom, Parallelisme, Interlocked.Read(ref Entrees), Interlocked.Read(ref Sorties), Interlocked.Read(ref Rejets),
            (long)traitement.TotalMilliseconds, (long)TimeSpan.FromTicks(Interlocked.Read(ref TicksAttenteSortie)).TotalMilliseconds,
            Math.Round(Math.Min(occupation, 1), 3));
    }
}

internal sealed class Etage<TEntree, TSortie>(IFiltre<TEntree, TSortie> filtre, int parallelisme, bool ordonne)
    : Etage(filtre.Nom, parallelisme, ordonne)
{
    public override Etage Copier() => new Etage<TEntree, TSortie>(filtre, Parallelisme, Ordonne);

    public override (Task Travail, object Sortie) Lancer(object entree, int capacite, ContexteExecution contexte)
    {
        var sortie = Channel.CreateBounded<Element<TSortie>>(capacite);
        var lecteur = (ChannelReader<Element<TEntree>>)entree;
        var travail = Parallelisme == 1 ? Sequentiel(lecteur, sortie.Writer, contexte)
            : Ordonne ? ParalleleOrdonne(lecteur, sortie.Writer, contexte)
            : ParalleleDesordonne(lecteur, sortie.Writer, contexte);
        return (Fermer(travail, sortie.Writer), sortie.Reader);
    }

    /// <summary>Quand l'étage finit (ou échoue), son tuyau de sortie se ferme : l'étage suivant le saura.</summary>
    private static async Task Fermer(Task travail, ChannelWriter<Element<TSortie>> sortie)
    {
        try
        {
            await travail;
            sortie.TryComplete();
        }
        catch (Exception e)
        {
            sortie.TryComplete(e);
            throw;
        }
    }

    private async Task Sequentiel(ChannelReader<Element<TEntree>> entree, ChannelWriter<Element<TSortie>> sortie, ContexteExecution contexte)
    {
        await foreach (var element in entree.ReadAllAsync(contexte.Annulation))
            if (await Traiter(element, contexte) is { } resultat)
                await Ecrire(sortie, resultat, contexte);
    }

    /// <summary>N copies du filtre lisent le même tuyau : les éléments sortent dans l'ordre où ils FINISSENT.</summary>
    private Task ParalleleDesordonne(ChannelReader<Element<TEntree>> entree, ChannelWriter<Element<TSortie>> sortie, ContexteExecution contexte) =>
        Task.WhenAll(Enumerable.Range(0, Parallelisme).Select(_ => Task.Run(() => Sequentiel(entree, sortie, contexte))));

    /// <summary>
    /// N copies du filtre, mais les éléments ressortent dans l'ordre d'ARRIVÉE : chacun reçoit un numéro, et un
    /// « re-séquenceur » retient ceux qui finissent en avance. Une fenêtre borne ce qui peut être en cours à la fois.
    /// </summary>
    private async Task ParalleleOrdonne(ChannelReader<Element<TEntree>> entree, ChannelWriter<Element<TSortie>> sortie, ContexteExecution contexte)
    {
        var fenetre = new SemaphoreSlim(Parallelisme * 4);
        var aTraiter = Channel.CreateBounded<(long Rang, Element<TEntree> Element)>(Parallelisme);
        var traites = Channel.CreateUnbounded<(long Rang, Element<TSortie>? Resultat)>();

        var distribution = Task.Run(async () =>
        {
            try
            {
                long rang = 0;
                await foreach (var element in entree.ReadAllAsync(contexte.Annulation))
                {
                    await fenetre.WaitAsync(contexte.Annulation);
                    await aTraiter.Writer.WriteAsync((rang++, element), contexte.Annulation);
                }
            }
            finally
            {
                aTraiter.Writer.TryComplete();
            }
        });

        var copies = Task.WhenAll(Enumerable.Range(0, Parallelisme).Select(_ => Task.Run(async () =>
        {
            await foreach (var (rang, element) in aTraiter.Reader.ReadAllAsync(contexte.Annulation))
                await traites.Writer.WriteAsync((rang, await Traiter(element, contexte)), contexte.Annulation);
        })));
        var fin = copies.ContinueWith(t => traites.Writer.TryComplete(t.Exception?.InnerException), TaskScheduler.Default);

        var enAvance = new Dictionary<long, Element<TSortie>?>();
        long attendu = 0;
        await foreach (var (rang, resultat) in traites.Reader.ReadAllAsync(contexte.Annulation))
        {
            enAvance[rang] = resultat;
            while (enAvance.Remove(attendu, out var suivant))
            {
                if (suivant is not null)
                    await Ecrire(sortie, suivant, contexte);
                attendu++;
                fenetre.Release();
            }
        }
        await Task.WhenAll(distribution, copies, fin);
    }

    /// <summary>Appelle le filtre et compte. Renvoie null si l'élément est rejeté.</summary>
    private async Task<Element<TSortie>?> Traiter(Element<TEntree> element, ContexteExecution contexte)
    {
        Interlocked.Increment(ref Entrees);
        var debut = Stopwatch.GetTimestamp();
        var issue = await filtre.Traiter(element.Valeur, contexte.Annulation);
        Interlocked.Add(ref TicksTraitement, Stopwatch.GetElapsedTime(debut).Ticks);

        if (issue.EstRejet)
        {
            Interlocked.Increment(ref Rejets);
            contexte.Rejeter(new Rejet(element.Ligne, Nom, issue.Motif!, issue.Detail));
            return null;
        }
        Interlocked.Increment(ref Sorties);
        return new Element<TSortie>(element.Ligne, issue.Valeur!);
    }

    /// <summary>Écrire dans un tuyau plein BLOQUE : l'étage attend que le suivant ait de la place. C'est la contre-pression.</summary>
    private async Task Ecrire(ChannelWriter<Element<TSortie>> sortie, Element<TSortie> element, ContexteExecution contexte)
    {
        var debut = Stopwatch.GetTimestamp();
        await sortie.WriteAsync(element, contexte.Annulation);
        Interlocked.Add(ref TicksAttenteSortie, Stopwatch.GetElapsedTime(debut).Ticks);
    }
}
