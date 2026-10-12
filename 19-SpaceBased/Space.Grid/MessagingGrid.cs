namespace Space.Grid;

public enum RoutingMode
{
    /// <summary>A key always goes to the same unit (hashing): a single unit writes each piece of data, no collision.</summary>
    Affinity,

    /// <summary>Each request goes to the next unit: load well spread, but two units may write the same data.</summary>
    RoundRobin
}

public sealed class NoUnitException() : Exception("No processing unit available.");

/// <summary>
/// The messaging grid: the entry point of the requests. It knows the active units and chooses the one that will handle
/// each request. If there is none left, it starts one (cold start).
/// </summary>
public sealed class MessagingGrid
{
    private readonly List<ProcessingUnit> _units = [];
    private readonly Lock _lock = new();
    private int _turn;

    public RoutingMode Mode { get; set; } = RoutingMode.Affinity;

    /// <summary>Provided by the deployment manager.</summary>
    internal Func<Task>? ColdStart { get; set; }

    public IReadOnlyList<ProcessingUnit> Units()
    {
        lock (_lock) return _units.ToList();
    }

    internal void Add(ProcessingUnit unit)
    {
        lock (_lock) _units.Add(unit);
    }

    internal void Remove(ProcessingUnit unit)
    {
        lock (_lock) _units.Remove(unit);
    }

    public async Task<(object Response, string Unit)> Send(IRequest request)
    {
        for (var attempt = 0; ; attempt++)
        {
            var unit = Choose(request.Key);
            if (unit is null && ColdStart is not null)
            {
                await ColdStart();
                unit = Choose(request.Key);
            }
            if (unit is null)
                throw new NoUnitException();

            try
            {
                return (await unit.Process(request), unit.Id);
            }
            catch (UnitFailedException) when (attempt < 3)
            {
                // The unit went down before handling the request: hand it to another one.
            }
        }
    }

    private ProcessingUnit? Choose(string key)
    {
        lock (_lock)
        {
            if (_units.Count == 0)
                return null;
            return Mode == RoutingMode.RoundRobin
                ? _units[(int)((uint)Interlocked.Increment(ref _turn) % _units.Count)]
                // "Rendezvous" hashing: the key goes to the unit that gets the best score with it. Adding or removing
                // a unit only moves the keys of that unit (a modulo would redistribute almost all of them).
                : _units.MaxBy(u => Hash($"{key}/{u.Id}"))!;
        }
    }

    /// <summary>FNV-1a: stable from one run to the next, unlike string.GetHashCode.</summary>
    internal static uint Hash(string text)
    {
        var h = 2166136261u;
        foreach (var c in text)
            h = (h ^ c) * 16777619u;
        return h;
    }
}
