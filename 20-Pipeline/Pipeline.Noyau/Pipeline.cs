using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Pipeline.Noyau;

public enum StatutExecution { Termine, Echoue, Annule }

/// <summary>Le compte rendu d'une exécution.</summary>
public sealed record Bilan(
    StatutExecution Statut,
    string? Erreur,
    long Lignes,
    long Sorties,
    long Rejets,
    IReadOnlyDictionary<string, long> RejetsParMotif,
    IReadOnlyList<Rejet> PremiersRejets,
    long DureeMs,
    long MaxEnTransit,
    IReadOnlyList<EtatEtage> Etages)
{
    /// <summary>L'étage le plus occupé : c'est lui qui fixe le débit de tout le pipeline.</summary>
    public string? Goulot => Etages.Count == 0 ? null : Etages.MaxBy(e => e.Occupation)!.Nom;

    public long DebitParSeconde => DureeMs == 0 ? Lignes : Lignes * 1000 / DureeMs;
}

public static class Pipeline
{
    /// <summary>Commence un pipeline dont la source produit des <typeparamref name="TSource"/>.</summary>
    public static Constructeur<TSource, TSource> Depuis<TSource>(int capaciteTuyaux = 64) => new(capaciteTuyaux, []);
}

/// <summary>On assemble un pipeline filtre par filtre ; le compilateur vérifie que la sortie de l'un est l'entrée du suivant.</summary>
public sealed class Constructeur<TSource, TCourant>
{
    private readonly int _capacite;
    private readonly IReadOnlyList<Etage> _etages;

    internal Constructeur(int capacite, IReadOnlyList<Etage> etages)
    {
        _capacite = capacite;
        _etages = etages;
    }

    /// <param name="parallelisme">Nombre de copies du filtre qui travaillent en même temps (filtre sans état uniquement).</param>
    /// <param name="ordonne">Avec plusieurs copies : faire ressortir les éléments dans leur ordre d'arrivée.</param>
    public Constructeur<TSource, TSuivant> Puis<TSuivant>(IFiltre<TCourant, TSuivant> filtre, int parallelisme = 1, bool ordonne = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelisme, 1);
        return new Constructeur<TSource, TSuivant>(_capacite, [.. _etages, new Etage<TCourant, TSuivant>(filtre, parallelisme, ordonne)]);
    }

    public Pipeline<TSource, TCourant> Construire() => new(_capacite, _etages);
}

/// <summary>Un pipeline assemblé, réutilisable : chaque exécution crée ses propres tuyaux.</summary>
public sealed class Pipeline<TSource, TSortie>
{
    private readonly int _capacite;
    private readonly IReadOnlyList<Etage> _etages;

    internal Pipeline(int capacite, IReadOnlyList<Etage> etages)
    {
        _capacite = capacite;
        _etages = etages;
    }

    public IReadOnlyList<string> Filtres => _etages.Select(e => e.Nom).ToList();

    /// <summary>
    /// Fait passer toute la source dans le pipeline, en flux : les étages travaillent EN MÊME TEMPS, chacun sur un élément
    /// différent, et jamais plus de quelques centaines d'éléments ne sont en mémoire, quelle que soit la taille de la source.
    /// </summary>
    public async Task<Bilan> Executer(IAsyncEnumerable<TSource> source, Action<TSortie>? puits = null, CancellationToken annulation = default)
    {
        // Chaque exécution a ses propres compteurs : on travaille sur des copies des étages.
        var etages = _etages.Select(e => e.Copier()).ToList();
        using var arret = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        var rejetsParMotif = new ConcurrentDictionary<string, long>();
        var premiersRejets = new ConcurrentQueue<Rejet>();
        var contexte = new ContexteExecution(arret.Token, rejet =>
        {
            rejetsParMotif.AddOrUpdate(rejet.Motif, 1, (_, n) => n + 1);
            if (premiersRejets.Count < 100)
                premiersRejets.Enqueue(rejet);
        });
        var chrono = Stopwatch.StartNew();
        long lignes = 0, sorties = 0;

        var tuyauSource = Channel.CreateBounded<Element<TSource>>(_capacite);
        var taches = new List<Task>
        {
            Task.Run(async () =>
            {
                try
                {
                    await foreach (var valeur in source.WithCancellation(arret.Token))
                    {
                        contexte.Entrer();
                        await tuyauSource.Writer.WriteAsync(new Element<TSource>(++lignes, valeur), arret.Token);
                    }
                    tuyauSource.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    tuyauSource.Writer.TryComplete(e);
                    throw;
                }
            })
        };

        object tuyau = tuyauSource.Reader;
        foreach (var etage in etages)
        {
            var (travail, sortie) = etage.Lancer(tuyau, _capacite, contexte);
            taches.Add(travail);
            tuyau = sortie;
        }

        taches.Add(Task.Run(async () =>
        {
            await foreach (var element in ((ChannelReader<Element<TSortie>>)tuyau).ReadAllAsync(arret.Token))
            {
                puits?.Invoke(element.Valeur);
                sorties++;
                contexte.Sortir();
            }
        }));

        // Le premier étage qui échoue arrête tous les autres.
        foreach (var tache in taches)
            _ = tache.ContinueWith(_ => arret.Cancel(), TaskContinuationOptions.OnlyOnFaulted);

        var (statut, erreur) = (StatutExecution.Termine, (string?)null);
        try
        {
            await Task.WhenAll(taches);
        }
        catch
        {
            var cause = taches.Where(t => t.IsFaulted).SelectMany(t => t.Exception!.InnerExceptions)
                .FirstOrDefault(e => e is not OperationCanceledException and not ChannelClosedException);
            (statut, erreur) = cause is not null ? (StatutExecution.Echoue, cause.Message)
                : annulation.IsCancellationRequested ? (StatutExecution.Annule, "Exécution annulée.")
                : (StatutExecution.Echoue, "Un étage a échoué.");
        }
        chrono.Stop();

        return new Bilan(statut, erreur, lignes, sorties, rejetsParMotif.Values.Sum(),
            rejetsParMotif.OrderByDescending(r => r.Value).ToDictionary(r => r.Key, r => r.Value),
            premiersRejets.OrderBy(r => r.Ligne).ToList(), chrono.ElapsedMilliseconds, contexte.MaxEnTransit,
            etages.Select(e => e.Etat(chrono.Elapsed)).ToList());
    }
}
