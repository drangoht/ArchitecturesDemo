using System.Collections.Concurrent;

namespace Cell.Router;

/// <summary>Marker of the assembly, for the tests.</summary>
public sealed class Marker;

/// <summary>A cell known to the control plane: where to reach it, how many customers it can take, which version runs there.</summary>
public sealed record CellInfo(string Id, string Url, int Capacity, bool Open = true, string Version = "1.0");

public sealed class CellRegistry
{
    private readonly ConcurrentDictionary<string, CellInfo> _cells = new(StringComparer.OrdinalIgnoreCase);

    public CellRegistry(IEnumerable<CellInfo> initial)
    {
        foreach (var cell in initial)
            _cells[cell.Id] = cell;
    }

    public IReadOnlyList<CellInfo> All() => _cells.Values.OrderBy(c => c.Id).ToList();

    public CellInfo? Find(string id) => _cells.GetValueOrDefault(id);

    public bool Add(CellInfo cell) => _cells.TryAdd(cell.Id, cell);

    public void Replace(CellInfo cell) => _cells[cell.Id] = cell;
}

/// <summary>
/// THE router's table: which customer lives in which cell. It is all the router knows, and all it needs.
/// In production: a small, highly available and replicated database (often cached in each instance of the router),
/// written by the control plane only. Here, in memory: restarting the router rebuilds it from the demo data.
/// </summary>
public sealed class PlacementTable
{
    private readonly ConcurrentDictionary<string, string> _placement = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _migrating = new(StringComparer.OrdinalIgnoreCase);

    public string? CellOf(string customer) => _placement.GetValueOrDefault(customer);

    public void Place(string customer, string cell) => _placement[customer] = cell;

    public int Count(string cell) => _placement.Values.Count(c => string.Equals(c, cell, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string> All() => new SortedDictionary<string, string>(_placement);

    public bool IsMigrating(string customer) => _migrating.ContainsKey(customer);

    public void StartMigration(string customer) => _migrating[customer] = 0;

    public void EndMigration(string customer) => _migrating.TryRemove(customer, out _);
}
