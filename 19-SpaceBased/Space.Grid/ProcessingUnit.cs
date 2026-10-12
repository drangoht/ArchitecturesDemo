namespace Space.Grid;

/// <summary>A business request. Its <see cref="Key"/> (e.g. the account) lets the messaging grid choose the unit.</summary>
public interface IRequest
{
    string Key { get; }
}

/// <summary>
/// A request sent by a unit to another one (e.g. the credit of a transfer): it takes no slot in the queue,
/// otherwise two full units waiting for each other would block.
/// </summary>
public interface IInternalRequest : IRequest;

public sealed class UnitFailedException(string unit) : Exception($"Unit {unit} has failed.");

/// <summary>The simulated processing time of a request (and, for the tests, a gate to hold back the requests in progress).</summary>
public sealed class LoadSimulation
{
    private volatile TaskCompletionSource? _gate;

    public TimeSpan ProcessingTime { get; set; }

    public void Close() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Open()
    {
        _gate?.TrySetResult();
        _gate = null;
    }

    internal async Task Pass()
    {
        if (_gate is { } gate)
            await gate.Task;
        if (ProcessingTime > TimeSpan.Zero)
            await Task.Delay(ProcessingTime);
    }
}

/// <summary>What the business code can do from a unit: read and write ITS replica, send a request to another unit.</summary>
public sealed class UnitContext(ProcessingUnit unit, MessagingGrid messages)
{
    public string Unit => unit.Id;

    public T? Read<T>(string key) where T : class => unit.Replica.Read(key)?.Value as T;

    public object Modify<T>(string key, Func<T?, (T? New, object Response)> decide) where T : class
    {
        unit.CheckAlive();
        return unit.Replica.Modify(key, decide).Response;
    }

    public async Task<object> Send(IInternalRequest request) => (await messages.Send(request)).Response;
}

/// <summary>
/// A processing unit: the business code AND a complete replica of the data, in memory. It is self-sufficient
/// to answer: neither database nor other unit to wait for. As many are added as needed when the load rises.
/// </summary>
public sealed class ProcessingUnit
{
    private readonly SemaphoreSlim _slots;
    private readonly Func<object, UnitContext, Task<object>> _processing;
    private readonly LoadSimulation _simulation;
    private readonly UnitContext _context;
    private int _inProgress, _pending;
    private long _processed;
    private volatile bool _alive = true;

    internal ProcessingUnit(string id, int capacity, Replica replica, Func<object, UnitContext, Task<object>> processing,
        LoadSimulation simulation, MessagingGrid messages)
    {
        Id = id;
        Capacity = capacity;
        Replica = replica;
        _slots = new SemaphoreSlim(capacity);
        _processing = processing;
        _simulation = simulation;
        _context = new UnitContext(this, messages);
    }

    public string Id { get; }
    public int Capacity { get; }
    public Replica Replica { get; }
    public int InProgress => Volatile.Read(ref _inProgress);
    public int Pending => Volatile.Read(ref _pending);
    public long Processed => Interlocked.Read(ref _processed);

    /// <summary>Requests in progress or pending, relative to the capacity: 1 = full.</summary>
    public double Load => (double)(InProgress + Pending) / Capacity;

    internal async Task<object> Process(object request)
    {
        var @internal = request is IInternalRequest;
        if (!@internal)
        {
            Interlocked.Increment(ref _pending);
            await _slots.WaitAsync();
            Interlocked.Decrement(ref _pending);
        }
        Interlocked.Increment(ref _inProgress);
        try
        {
            CheckAlive();
            if (!@internal)
                await _simulation.Pass();
            return await _processing(request, _context);
        }
        finally
        {
            Interlocked.Decrement(ref _inProgress);
            Interlocked.Increment(ref _processed);
            if (!@internal)
                _slots.Release();
        }
    }

    internal void CheckAlive()
    {
        if (!_alive)
            throw new UnitFailedException(Id);
    }

    internal void Kill() => _alive = false;

    /// <summary>Waits for the requests in progress to finish (clean stop).</summary>
    internal async Task Drain()
    {
        while (InProgress + Pending > 0)
            await Task.Delay(5);
    }
}
