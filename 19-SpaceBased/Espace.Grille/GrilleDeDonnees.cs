using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Espace.Grille;

/// <summary>
/// La grille de données : une réplique par unité de traitement, et la réplication entre elles.
/// Chaque écriture locale part dans une file, livrée aux autres répliques après <see cref="LatenceReplication"/>
/// (le réseau, en vrai). Pendant ce délai, les répliques divergent : c'est la fenêtre des collisions.
/// </summary>
public sealed class GrilleDeDonnees : IAsyncDisposable
{
    private readonly Dictionary<string, Replique> _repliques = new();

    // Livrer une modification à toutes les répliques, ou inscrire une nouvelle réplique : jamais en même temps.
    private readonly Lock _verrouLivraison = new();
    private readonly Channel<(string Origine, Modification Modification, DateTimeOffset Livrable)> _file = Channel.CreateUnbounded<(string, Modification, DateTimeOffset)>();
    private readonly Action<Modification> _versLaPompe;
    private readonly Action<string> _noter;
    private readonly ConcurrentDictionary<(string Cle, Version Version), byte> _perdues = new();
    private readonly CancellationTokenSource _arret = new();
    private readonly Task _boucle;
    private TaskCompletionSource _suspension = Termine();
    private int _enVol;

    public GrilleDeDonnees(Action<Modification> versLaPompe, Action<string> noter)
    {
        _versLaPompe = versLaPompe;
        _noter = noter;
        _boucle = Task.Run(Livrer);
    }

    public TimeSpan LatenceReplication { get; set; }

    /// <summary>Modifications publiées mais pas encore livrées à toutes les répliques.</summary>
    public int EnAttente => Volatile.Read(ref _enVol);

    /// <summary>Écritures acceptées par une unité (le client a reçu « OK ») puis écrasées par une écriture concurrente.</summary>
    public int MisesAJourPerdues => _perdues.Count;

    /// <summary>
    /// Crée la réplique d'une nouvelle unité, remplie par <paramref name="amorce"/> (la copie d'une autre réplique, ou la base).
    /// Sous le verrou de livraison : aucune modification ne peut passer entre la copie et l'inscription.
    /// </summary>
    public Replique CreerReplique(string unite, Func<IEnumerable<Entree>> amorce)
    {
        var replique = new Replique(unite, m => Publier(unite, m));
        lock (_verrouLivraison)
        {
            replique.Charger(amorce());
            _repliques[unite] = replique;
        }
        return replique;
    }

    public void RetirerReplique(string unite)
    {
        lock (_verrouLivraison)
            _repliques.Remove(unite);
    }

    public IReadOnlyList<Replique> Repliques()
    {
        lock (_verrouLivraison) return _repliques.Values.ToList();
    }

    private void Publier(string origine, Modification modification)
    {
        Interlocked.Increment(ref _enVol);
        _file.Writer.TryWrite((origine, modification, DateTimeOffset.UtcNow + LatenceReplication));
        // La base n'est pas sur le chemin de la requête : l'écriture part dans la pompe, on n'attend pas.
        _versLaPompe(modification);
    }

    private async Task Livrer()
    {
        try
        {
            await foreach (var (origine, modification, livrable) in _file.Reader.ReadAllAsync(_arret.Token))
            {
                var attente = livrable - DateTimeOffset.UtcNow;
                if (attente > TimeSpan.Zero)
                    await Task.Delay(attente, _arret.Token);
                await _suspension.Task.WaitAsync(_arret.Token);

                lock (_verrouLivraison)
                    foreach (var replique in _repliques.Values.Where(r => r.Unite != origine))
                        if (replique.Fusionner(modification).Perdue is { } perdue && _perdues.TryAdd((modification.Cle, perdue), 0))
                            _noter($"COLLISION sur {modification.Cle} : l'écriture {perdue} est perdue");
                Interlocked.Decrement(ref _enVol);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt
        }
    }

    /// <summary>Retient les livraisons (pour les tests : rendre une collision certaine, pas seulement probable).</summary>
    public void Suspendre() => Interlocked.CompareExchange(ref _suspension, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), _suspension);

    public void Reprendre() => _suspension.TrySetResult();

    /// <summary>Attend que toutes les modifications publiées aient été livrées : les répliques sont alors identiques.</summary>
    public async Task AttendreReplication()
    {
        while (EnAttente > 0)
            await Task.Delay(5);
    }

    public async ValueTask DisposeAsync()
    {
        await _arret.CancelAsync();
        Reprendre();
        await _boucle;
    }

    private static TaskCompletionSource Termine()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }
}
