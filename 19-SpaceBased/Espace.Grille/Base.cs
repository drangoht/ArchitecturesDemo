using System.Threading.Channels;

namespace Espace.Grille;

public sealed class BaseIndisponibleException() : Exception("La base de données est indisponible.");

/// <summary>
/// La base de données : la référence durable, mais lente. Dans cette architecture, AUCUNE requête ne l'attend :
/// elle n'est écrite que par la pompe, et lue qu'au démarrage à froid.
/// </summary>
public sealed class Base
{
    private readonly Dictionary<string, Entree> _lignes = new();
    private readonly Lock _verrou = new();
    private volatile bool _disponible = true;
    private long _ecritures, _lectures;

    public TimeSpan Latence { get; set; }
    public bool Disponible { get => _disponible; set => _disponible = value; }
    public long Ecritures => Interlocked.Read(ref _ecritures);

    /// <summary>Nombre de fois où toute la base a été lue (par le lecteur de données, au démarrage à froid).</summary>
    public long Lectures => Interlocked.Read(ref _lectures);

    public void Initialiser(IEnumerable<Entree> entrees)
    {
        lock (_verrou)
            foreach (var entree in entrees)
                _lignes[entree.Cle] = entree;
    }

    /// <summary>Même règle que les répliques : une écriture plus ancienne que la ligne stockée est ignorée.</summary>
    public async Task Ecrire(Modification m)
    {
        if (!Disponible)
            throw new BaseIndisponibleException();
        if (Latence > TimeSpan.Zero)
            await Task.Delay(Latence);
        lock (_verrou)
            if (!_lignes.TryGetValue(m.Cle, out var ligne) || m.Version > ligne.Version)
                _lignes[m.Cle] = new Entree(m.Cle, m.Valeur, m.Version);
        Interlocked.Increment(ref _ecritures);
    }

    public IReadOnlyList<Entree> Tout()
    {
        if (!Disponible)
            throw new BaseIndisponibleException();
        Interlocked.Increment(ref _lectures);
        lock (_verrou) return _lignes.Values.ToList();
    }

    public Entree? Lire(string cle)
    {
        lock (_verrou) return _lignes.GetValueOrDefault(cle);
    }
}

/// <summary>
/// La pompe de données et son écrivain : les modifications des unités s'y accumulent et sont écrites dans la base
/// en arrière-plan, au rythme de la base. Si la base tombe, la file grossit, les requêtes continuent ; quand elle revient,
/// la file se vide. (En production, la pompe est une file de messages durable : Kafka, RabbitMQ…)
/// </summary>
public sealed class PompeDeDonnees : IAsyncDisposable
{
    private readonly Base _base;
    private readonly Channel<Modification> _file = Channel.CreateUnbounded<Modification>();
    private readonly CancellationTokenSource _arret = new();
    private readonly Task _ecrivain;
    private int _enAttente;

    public PompeDeDonnees(Base @base)
    {
        _base = @base;
        _ecrivain = Task.Run(Ecrire);
    }

    /// <summary>Modifications pas encore écrites dans la base : ce dont la base est « en retard ».</summary>
    public int EnAttente => Volatile.Read(ref _enAttente);

    public void Deposer(Modification modification)
    {
        Interlocked.Increment(ref _enAttente);
        _file.Writer.TryWrite(modification);
    }

    private async Task Ecrire()
    {
        try
        {
            await foreach (var modification in _file.Reader.ReadAllAsync(_arret.Token))
            {
                while (true)
                {
                    try
                    {
                        await _base.Ecrire(modification);
                        break;
                    }
                    catch (BaseIndisponibleException)
                    {
                        await Task.Delay(50, _arret.Token);  // on garde la modification et on réessaie
                    }
                }
                Interlocked.Decrement(ref _enAttente);
            }
        }
        catch (OperationCanceledException)
        {
            // arrêt
        }
    }

    public async Task AttendreVidage(CancellationToken annulation = default)
    {
        while (EnAttente > 0)
            await Task.Delay(5, annulation);
    }

    public async ValueTask DisposeAsync()
    {
        await _arret.CancelAsync();
        await _ecrivain;
    }
}
