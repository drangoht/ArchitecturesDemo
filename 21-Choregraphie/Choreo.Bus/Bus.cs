using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Choreo.Bus;

/// <summary>
/// Un fait qui s'est produit, au passé (« FondsReserves », pas « ReserverFonds »). Celui qui le publie ne sait pas qui l'écoute.
/// </summary>
public abstract record Evenement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Le processus auquel il appartient (ici, l'identifiant du virement) : le seul fil qui relie les événements.</summary>
    public string Correlation { get; init; } = "";

    /// <summary>L'événement qui l'a provoqué (null s'il vient de l'extérieur : une requête, une minuterie, un humain).</summary>
    public Guid? Cause { get; init; }

    public string Emetteur { get; init; } = "";
    public DateTimeOffset Date { get; init; }

    public string Type => GetType().Name;

    /// <summary>Un nouvel événement, en réaction à celui-ci : même corrélation, et celui-ci pour cause.</summary>
    public T Suite<T>(T suivant, string emetteur) where T : Evenement =>
        suivant with { Correlation = Correlation, Cause = Id, Emetteur = emetteur };
}

public sealed record OptionsBus
{
    /// <summary>Tentatives de livraison d'un événement à un abonné, avant de le mettre aux lettres mortes.</summary>
    public int MaxTentatives { get; init; } = 4;

    /// <summary>Probabilité de livrer un événement DEUX FOIS (le « au moins une fois » des vrais courtiers, en exagéré).</summary>
    public double ProbabiliteDoublon { get; init; }

    public int Graine { get; init; } = 42;
}

public sealed record LettreMorte(string Abonne, Evenement Evenement, int Tentatives, string Erreur);

public sealed record EtatAbonne(string Abonne, IReadOnlyList<string> Ecoute, int EnAttente, long Livres, long Echecs);

/// <summary>
/// Le courtier : chaque abonné a SA file et la vide à son rythme ; un abonné lent ou en panne ne retarde pas les autres.
/// Un traitement qui échoue est réessayé, puis mis aux lettres mortes. Tout ce qui est publié est gardé au journal.
/// </summary>
public sealed class Bus : IAsyncDisposable
{
    private readonly OptionsBus _options;
    private readonly TimeProvider _horloge;
    private readonly Dictionary<string, Abonnement> _abonnements = new();
    private readonly List<Evenement> _journal = [];
    private readonly ConcurrentQueue<LettreMorte> _lettresMortes = new();
    private readonly Lock _verrou = new();
    private readonly Random _hasard;
    private readonly CancellationTokenSource _arret = new();
    private int _enCours;

    public Bus(OptionsBus? options = null, TimeProvider? horloge = null)
    {
        _options = options ?? new OptionsBus();
        _horloge = horloge ?? TimeProvider.System;
        _hasard = new Random(_options.Graine);
    }

    /// <summary>S'abonner à un type d'événement. Un abonné peut écouter plusieurs types : ils arrivent dans sa file, dans l'ordre.</summary>
    public void Abonner<T>(string abonne, Func<T, Task> traiter) where T : Evenement
    {
        lock (_verrou)
        {
            if (!_abonnements.TryGetValue(abonne, out var abonnement))
            {
                abonnement = new Abonnement(abonne);
                abonnement.Boucle = Task.Run(() => Livrer(abonnement));
                _abonnements[abonne] = abonnement;
            }
            abonnement.Traitements[typeof(T)] = e => traiter((T)e);
        }
    }

    public void Abonner<T>(string abonne, Action<T> traiter) where T : Evenement =>
        Abonner<T>(abonne, e =>
        {
            traiter(e);
            return Task.CompletedTask;
        });

    /// <summary>Publier : déposer dans la file de chaque abonné intéressé, sans attendre personne.</summary>
    public T Publier<T>(T evenement) where T : Evenement
    {
        var date = evenement.Date == default ? evenement with { Date = _horloge.GetUtcNow() } : evenement;
        lock (_verrou)
        {
            _journal.Add(date);
            foreach (var abonnement in _abonnements.Values.Where(a => a.Traitements.ContainsKey(date.GetType())))
            {
                Deposer(abonnement, date);
                if (_options.ProbabiliteDoublon > 0 && _hasard.NextDouble() < _options.ProbabiliteDoublon)
                    Deposer(abonnement, date);  // livré deux fois : l'abonné doit s'en apercevoir
            }
        }
        return date;
    }

    private void Deposer(Abonnement abonnement, Evenement evenement)
    {
        Interlocked.Increment(ref _enCours);
        abonnement.File.Writer.TryWrite(evenement);
    }

    private async Task Livrer(Abonnement abonnement)
    {
        try
        {
            await foreach (var evenement in abonnement.File.Reader.ReadAllAsync(_arret.Token))
            {
                Func<Evenement, Task> traitement;
                lock (_verrou)
                    traitement = abonnement.Traitements[evenement.GetType()];

                for (var tentative = 1; ; tentative++)
                {
                    try
                    {
                        await traitement(evenement);
                        Interlocked.Increment(ref abonnement.Livres);
                        break;
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Interlocked.Increment(ref abonnement.Echecs);
                        if (tentative >= _options.MaxTentatives)
                        {
                            // Personne n'en saura rien, sauf qui lit les lettres mortes : le processus reste en plan.
                            _lettresMortes.Enqueue(new LettreMorte(abonnement.Nom, evenement, tentative, e.Message));
                            break;
                        }
                        await Task.Delay(10 * tentative, _arret.Token);
                    }
                }
                Interlocked.Decrement(ref _enCours);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt
        }
    }

    /// <summary>Tous les événements publiés, dans l'ordre.</summary>
    public IReadOnlyList<Evenement> Journal()
    {
        lock (_verrou) return _journal.ToList();
    }

    public IReadOnlyList<Evenement> Journal(string correlation)
    {
        lock (_verrou) return _journal.Where(e => e.Correlation == correlation).ToList();
    }

    public IReadOnlyList<LettreMorte> LettresMortes() => _lettresMortes.ToList();

    public IReadOnlyList<EtatAbonne> Abonnes()
    {
        lock (_verrou)
            return _abonnements.Values.OrderBy(a => a.Nom).Select(a => new EtatAbonne(a.Nom,
                a.Traitements.Keys.Select(t => t.Name).Order().ToList(), a.File.Reader.Count,
                Interlocked.Read(ref a.Livres), Interlocked.Read(ref a.Echecs))).ToList();
    }

    /// <summary>Attend que plus rien ne circule : toutes les réactions en chaîne sont terminées.</summary>
    public async Task AttendreCalme(TimeSpan? delai = null)
    {
        var limite = DateTime.UtcNow + (delai ?? TimeSpan.FromSeconds(10));
        while (Volatile.Read(ref _enCours) > 0)
        {
            if (DateTime.UtcNow > limite)
                throw new TimeoutException("Le bus ne s'est pas calmé.");
            await Task.Delay(5);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _arret.CancelAsync();
        List<Task> boucles;
        lock (_verrou) boucles = _abonnements.Values.Select(a => a.Boucle!).ToList();
        await Task.WhenAll(boucles);
    }

    private sealed class Abonnement(string nom)
    {
        public string Nom { get; } = nom;
        public Dictionary<Type, Func<Evenement, Task>> Traitements { get; } = new();
        public Channel<Evenement> File { get; } = Channel.CreateUnbounded<Evenement>();
        public Task? Boucle { get; set; }
        public long Livres, Echecs;
    }
}
