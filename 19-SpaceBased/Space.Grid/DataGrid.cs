using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Space.Grid;

/// <summary>
/// The data grid: one replica per processing unit, and the replication between them.
/// Each local write goes into a queue, delivered to the other replicas after <see cref="ReplicationLatency"/>
/// (the network, for real). During that delay, the replicas diverge: that is the collision window.
/// </summary>
public sealed class DataGrid : IAsyncDisposable
{
    private readonly Dictionary<string, Replica> _replicas = new();

    // Delivering a modification to all the replicas, or registering a new replica: never at the same time.
    private readonly Lock _deliveryLock = new();
    private readonly Channel<(string Origin, Modification Modification, DateTimeOffset Deliverable)> _queue = Channel.CreateUnbounded<(string, Modification, DateTimeOffset)>();
    private readonly Action<Modification> _toThePump;
    private readonly Action<string> _note;
    private readonly ConcurrentDictionary<(string Key, Version Version), byte> _lost = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private TaskCompletionSource _suspension = Completed();
    private int _inFlight;

    public DataGrid(Action<Modification> toThePump, Action<string> note)
    {
        _toThePump = toThePump;
        _note = note;
        _loop = Task.Run(Deliver);
    }

    public TimeSpan ReplicationLatency { get; set; }

    /// <summary>Modifications published but not yet delivered to all the replicas.</summary>
    public int Pending => Volatile.Read(ref _inFlight);

    /// <summary>Writes accepted by a unit (the client received "OK") then overwritten by a concurrent write.</summary>
    public int LostUpdates => _lost.Count;

    /// <summary>
    /// Creates the replica of a new unit, filled by <paramref name="seed"/> (the copy of another replica, or the database).
    /// Under the delivery lock: no modification can slip in between the copy and the registration.
    /// </summary>
    public Replica CreateReplica(string unit, Func<IEnumerable<Entry>> seed)
    {
        var replica = new Replica(unit, m => Publish(unit, m));
        lock (_deliveryLock)
        {
            replica.Load(seed());
            _replicas[unit] = replica;
        }
        return replica;
    }

    public void RemoveReplica(string unit)
    {
        lock (_deliveryLock)
            _replicas.Remove(unit);
    }

    public IReadOnlyList<Replica> Replicas()
    {
        lock (_deliveryLock) return _replicas.Values.ToList();
    }

    private void Publish(string origin, Modification modification)
    {
        Interlocked.Increment(ref _inFlight);
        _queue.Writer.TryWrite((origin, modification, DateTimeOffset.UtcNow + ReplicationLatency));
        // The database is not on the request path: the write goes into the pump, we do not wait.
        _toThePump(modification);
    }

    private async Task Deliver()
    {
        try
        {
            await foreach (var (origin, modification, deliverable) in _queue.Reader.ReadAllAsync(_stopping.Token))
            {
                var wait = deliverable - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, _stopping.Token);
                await _suspension.Task.WaitAsync(_stopping.Token);

                lock (_deliveryLock)
                    foreach (var replica in _replicas.Values.Where(r => r.Unit != origin))
                        if (replica.Merge(modification).Lost is { } lost && _lost.TryAdd((modification.Key, lost), 0))
                            _note($"COLLISION on {modification.Key}: write {lost} is lost");
                Interlocked.Decrement(ref _inFlight);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    /// <summary>Holds back the deliveries (for the tests: make a collision certain, not only likely).</summary>
    public void Suspend() => Interlocked.CompareExchange(ref _suspension, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), _suspension);

    public void Resume() => _suspension.TrySetResult();

    /// <summary>Waits until all the published modifications have been delivered: the replicas are then identical.</summary>
    public async Task WaitForReplication()
    {
        while (Pending > 0)
            await Task.Delay(5);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        Resume();
        await _loop;
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }
}
