using System.Threading.Channels;

namespace Space.Grid;

public sealed class DatabaseUnavailableException() : Exception("The database is unavailable.");

/// <summary>
/// The database: the durable reference, but slow. In this architecture, NO request waits for it:
/// it is only written by the pump, and only read at cold start.
/// </summary>
public sealed class Database
{
    private readonly Dictionary<string, Entry> _rows = new();
    private readonly Lock _lock = new();
    private volatile bool _available = true;
    private long _writes, _reads;

    public TimeSpan Latency { get; set; }
    public bool Available { get => _available; set => _available = value; }
    public long Writes => Interlocked.Read(ref _writes);

    /// <summary>Number of times the whole database was read (by the data reader, at cold start).</summary>
    public long Reads => Interlocked.Read(ref _reads);

    public void Initialize(IEnumerable<Entry> entries)
    {
        lock (_lock)
            foreach (var entry in entries)
                _rows[entry.Key] = entry;
    }

    /// <summary>Same rule as the replicas: a write older than the stored row is ignored.</summary>
    public async Task Write(Modification m)
    {
        if (!Available)
            throw new DatabaseUnavailableException();
        if (Latency > TimeSpan.Zero)
            await Task.Delay(Latency);
        lock (_lock)
            if (!_rows.TryGetValue(m.Key, out var row) || m.Version > row.Version)
                _rows[m.Key] = new Entry(m.Key, m.Value, m.Version);
        Interlocked.Increment(ref _writes);
    }

    public IReadOnlyList<Entry> All()
    {
        if (!Available)
            throw new DatabaseUnavailableException();
        Interlocked.Increment(ref _reads);
        lock (_lock) return _rows.Values.ToList();
    }

    public Entry? Read(string key)
    {
        lock (_lock) return _rows.GetValueOrDefault(key);
    }
}

/// <summary>
/// The data pump and its writer: the modifications of the units pile up in it and are written to the database
/// in the background, at the database's pace. If the database goes down, the queue grows, the requests keep going; when it comes back,
/// the queue empties. (In production, the pump is a durable message queue: Kafka, RabbitMQ…)
/// </summary>
public sealed class DataPump : IAsyncDisposable
{
    private readonly Database _database;
    private readonly Channel<Modification> _queue = Channel.CreateUnbounded<Modification>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _writer;
    private int _pending;

    public DataPump(Database database)
    {
        _database = database;
        _writer = Task.Run(Write);
    }

    /// <summary>Modifications not yet written to the database: how far "behind" the database is.</summary>
    public int Pending => Volatile.Read(ref _pending);

    public void Push(Modification modification)
    {
        Interlocked.Increment(ref _pending);
        _queue.Writer.TryWrite(modification);
    }

    private async Task Write()
    {
        try
        {
            await foreach (var modification in _queue.Reader.ReadAllAsync(_stopping.Token))
            {
                while (true)
                {
                    try
                    {
                        await _database.Write(modification);
                        break;
                    }
                    catch (DatabaseUnavailableException)
                    {
                        await Task.Delay(50, _stopping.Token);  // keep the modification and try again
                    }
                }
                Interlocked.Decrement(ref _pending);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    public async Task WaitUntilDrained(CancellationToken cancellation = default)
    {
        while (Pending > 0)
            await Task.Delay(5, cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _writer;
    }
}
