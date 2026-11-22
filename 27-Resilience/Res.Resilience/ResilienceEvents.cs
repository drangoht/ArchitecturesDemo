namespace Res.Resilience;

/// <summary>What the strategies did, in order (a stand-in for logs and metrics).</summary>
public class ResilienceEvents(TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly List<ResilienceEvent> _events = [];

    public void Add(string strategy, string detail)
    {
        lock (_gate)
            _events.Add(new ResilienceEvent(clock.GetUtcNow(), strategy, detail));
    }

    public IReadOnlyList<ResilienceEvent> Latest(int count)
    {
        lock (_gate)
            return [.. _events.TakeLast(count)];
    }

    public int Count(string strategy)
    {
        lock (_gate)
            return _events.Count(e => e.Strategy == strategy);
    }
}

public record ResilienceEvent(DateTimeOffset At, string Strategy, string Detail);
