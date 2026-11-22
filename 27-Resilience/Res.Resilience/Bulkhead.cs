namespace Res.Resilience;

/// <summary>
/// Bulkhead: at most N calls in progress to this service, M more waiting, the others refused at once.
/// A slow service then ties up N threads and connections, not all of them: the rest of the application keeps going.
/// Shared by every call (a singleton), like the breaker.
/// </summary>
public class Bulkhead(BulkheadOptions options, ResilienceEvents events)
{
    private readonly SemaphoreSlim _slots = new(options.MaxConcurrency, options.MaxConcurrency);
    private readonly Lock _gate = new();
    private int _queued;

    public int InProgress => options.MaxConcurrency - _slots.CurrentCount;

    public int Queued
    {
        get { lock (_gate) return _queued; }
    }

    public async Task<IDisposable> Enter(CancellationToken ct)
    {
        if (!_slots.Wait(0))
        {
            lock (_gate)
            {
                if (_queued >= options.MaxQueue)
                {
                    events.Add("bulkhead", $"rejected: {options.MaxConcurrency} in progress, {_queued} waiting");
                    throw new BulkheadRejectedException();
                }
                _queued++;
            }
            try
            {
                await _slots.WaitAsync(ct);
            }
            finally
            {
                lock (_gate)
                    _queued--;
            }
        }
        return new Slot(_slots);
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
        }
    }
}

public class BulkheadHandler(Bulkhead bulkhead) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var slot = await bulkhead.Enter(ct);
        return await base.SendAsync(request, ct);
    }
}
