namespace Space.Grid;

/// <summary>
/// The version of an entry: a counter, and the unit that wrote it (to break the tie between two simultaneous writes).
/// All the replicas and the database apply the same rule, "the greatest version wins": they end up identical.
/// </summary>
public readonly record struct Version(long Number, string Unit) : IComparable<Version>
{
    public static readonly Version Zero = new(0, "");

    public int CompareTo(Version other) =>
        Number != other.Number ? Number.CompareTo(other.Number) : string.CompareOrdinal(Unit, other.Unit);

    public static bool operator >(Version a, Version b) => a.CompareTo(b) > 0;
    public static bool operator <(Version a, Version b) => a.CompareTo(b) < 0;

    public override string ToString() => $"v{Number}@{Unit}";
}

/// <summary>A piece of data of the grid. The value is an immutable object (a record): it is replaced, not modified.</summary>
public sealed record Entry(string Key, object Value, Version Version);

/// <summary>A write made by a unit, sent to the other replicas and to the data pump.</summary>
public sealed record Modification(string Key, object Value, Version Version, Version Previous);

/// <summary>What the arrival of a modification in a replica resulted in.</summary>
public readonly record struct MergeResult(bool Applied, Version? Lost);

/// <summary>
/// The IN-MEMORY copy of all the data, specific to a processing unit. The unit reads and writes in it without ever
/// waiting for the database; each write is then broadcast to the other replicas, asynchronously.
/// </summary>
public sealed class Replica
{
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly Lock _lock = new();
    private readonly Action<Modification> _publish;

    internal Replica(string unit, Action<Modification> publish)
    {
        Unit = unit;
        _publish = publish;
    }

    public string Unit { get; }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public Entry? Read(string key)
    {
        lock (_lock) return _entries.GetValueOrDefault(key);
    }

    public IReadOnlyList<Entry> All()
    {
        lock (_lock) return _entries.Values.ToList();
    }

    /// <summary>
    /// Read, decide, write: atomic IN THIS REPLICA. Nothing prevents another unit from doing the same at the same moment
    /// on its own replica: that is the source of the collisions.
    /// </summary>
    internal (object Response, Modification? Modification) Modify<T>(string key, Func<T?, (T? New, object Response)> decide)
        where T : class
    {
        lock (_lock)
        {
            var current = _entries.GetValueOrDefault(key);
            var (@new, response) = decide(current?.Value as T);
            if (@new is null)
                return (response, null);

            var previous = current?.Version ?? Version.Zero;
            var modification = new Modification(key, @new, new Version(previous.Number + 1, Unit), previous);
            _entries[key] = new Entry(key, @new, modification.Version);
            // Published under the lock: the modifications of a unit leave in the order in which they were made.
            _publish(modification);
            return (response, modification);
        }
    }

    /// <summary>
    /// A modification coming from another unit. Normal case: it starts from the version we have (<c>Previous</c>).
    /// Otherwise, two units modified the same entry at the same time without seeing each other: a COLLISION. The greatest version
    /// wins everywhere, the other write is lost, although its client received "OK".
    /// </summary>
    internal MergeResult Merge(Modification m)
    {
        lock (_lock)
        {
            var local = _entries.GetValueOrDefault(m.Key)?.Version ?? Version.Zero;
            if (m.Version == local)
                return new MergeResult(false, null);  // already known (initial copy of a new replica)

            var concurrent = local != m.Previous && local.Unit != "" && local.Unit != m.Version.Unit;
            if (m.Version > local)
            {
                _entries[m.Key] = new Entry(m.Key, m.Value, m.Version);
                return new MergeResult(true, concurrent ? local : null);
            }
            return new MergeResult(false, concurrent ? m.Version : null);
        }
    }

    internal void Load(IEnumerable<Entry> entries)
    {
        lock (_lock)
            foreach (var entry in entries)
                _entries[entry.Key] = entry;
    }
}
