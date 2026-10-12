using System.Collections.Concurrent;

namespace Space.Grid;

public sealed class PlatformOptions
{
    public int CapacityPerUnit { get; set; } = 4;
    public int UnitsAtStartup { get; set; } = 2;
    public int MinUnits { get; set; } = 1;
    public int MaxUnits { get; set; } = 6;

    /// <summary>Average load (requests in progress or pending / capacity) beyond which a unit is added.</summary>
    public double HighThreshold { get; set; } = 0.75;

    /// <summary>Average load below which one is removed.</summary>
    public double LowThreshold { get; set; } = 0.25;

    public int ReplicationLatencyMs { get; set; }
    public int DatabaseLatencyMs { get; set; }
    public int ProcessingTimeMs { get; set; }
}

public sealed record UnitState(string Id, int InProgress, int Pending, long Processed, int Entries);

public sealed record PlatformState(
    RoutingMode Routing,
    IReadOnlyList<UnitState> Units,
    int ReplicationPending,
    int LostUpdates,
    int PumpPending,
    bool DatabaseAvailable,
    long DatabaseWrites,
    long DatabaseReads,
    int ColdStarts);

/// <summary>
/// The assembled "virtualized middleware": data grid, messaging grid, pump, database, deployment manager.
/// The business code only brings one function to it: handle a request in the context of a unit.
/// </summary>
public sealed class Platform : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _log = new();

    public Platform(PlatformOptions options, Func<object, UnitContext, Task<object>> processing)
    {
        Options = options;
        Database = new Database { Latency = TimeSpan.FromMilliseconds(options.DatabaseLatencyMs) };
        Pump = new DataPump(Database);
        Data = new DataGrid(Pump.Push, Note) { ReplicationLatency = TimeSpan.FromMilliseconds(options.ReplicationLatencyMs) };
        Simulation = new LoadSimulation { ProcessingTime = TimeSpan.FromMilliseconds(options.ProcessingTimeMs) };
        Messages = new MessagingGrid();
        Deployment = new DeploymentManager(this, processing);
    }

    public PlatformOptions Options { get; }
    public Database Database { get; }
    public DataPump Pump { get; }
    public DataGrid Data { get; }
    public MessagingGrid Messages { get; }
    public LoadSimulation Simulation { get; }
    public DeploymentManager Deployment { get; }

    /// <summary>Fills the database, then starts the first units (the first one cold, from the database).</summary>
    public async Task Start(IEnumerable<Entry> initialData)
    {
        Database.Initialize(initialData);
        for (var i = 0; i < Options.UnitsAtStartup; i++)
            await Deployment.Start();
    }

    public Task<(object Response, string Unit)> Send(IRequest request) => Messages.Send(request);

    /// <summary>Waits until everything is at rest: identical replicas, database up to date.</summary>
    public async Task WaitForStabilization()
    {
        await Data.WaitForReplication();
        await Pump.WaitUntilDrained();
    }

    public PlatformState State() => new(
        Messages.Mode,
        Messages.Units().Select(u => new UnitState(u.Id, u.InProgress, u.Pending, u.Processed, u.Replica.Count)).ToList(),
        Data.Pending, Data.LostUpdates, Pump.Pending, Database.Available, Database.Writes, Database.Reads,
        Deployment.ColdStarts);

    /// <summary>The latest events (units started or stopped, collisions), from the most recent to the oldest.</summary>
    public IReadOnlyList<string> Log() => _log.Reverse().ToList();

    internal void Note(string @event)
    {
        _log.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {@event}");
        while (_log.Count > 200)
            _log.TryDequeue(out _);
    }

    public async ValueTask DisposeAsync()
    {
        Simulation.Open();
        await Data.DisposeAsync();
        await Pump.DisposeAsync();
    }
}
