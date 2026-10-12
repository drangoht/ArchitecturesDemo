namespace Space.Grid;

public sealed class ColdStartImpossibleException(int behind)
    : Exception($"Cold start impossible for now: the database is still {behind} writes behind.");

/// <summary>
/// The deployment manager: it starts and stops the processing units according to the load (elasticity).
/// A new unit receives a copy of the grid from an existing unit; if none is left, from the database
/// (cold start, through the data reader).
/// </summary>
public sealed class DeploymentManager
{
    private readonly Platform _platform;
    private readonly Func<object, UnitContext, Task<object>> _processing;
    private readonly SemaphoreSlim _singleColdStart = new(1);
    private int _number, _coldStarts;

    internal DeploymentManager(Platform platform, Func<object, UnitContext, Task<object>> processing)
    {
        _platform = platform;
        _processing = processing;
        platform.Messages.ColdStart = StartCold;
    }

    public int ColdStarts => Volatile.Read(ref _coldStarts);

    public async Task<ProcessingUnit> Start()
    {
        var id = $"u{Interlocked.Increment(ref _number)}";
        var source = _platform.Messages.Units().FirstOrDefault();
        Replica replica;
        if (source is not null)
        {
            replica = _platform.Data.CreateReplica(id, source.Replica.All);
            _platform.Note($"{id} started: grid copied from {source.Id} ({replica.Count} entries)");
        }
        else
        {
            // DATA READER. First wait for the pump to have written everything: otherwise we would load stale values,
            // and the late writes would then overwrite in the database those of the new unit.
            // Database down: impossible to start cold, the request fails (503) instead of waiting forever.
            if (!_platform.Database.Available)
                throw new DatabaseUnavailableException();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _platform.Pump.WaitUntilDrained(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new ColdStartImpossibleException(_platform.Pump.Pending);
            }
            replica = _platform.Data.CreateReplica(id, _platform.Database.All);
            Interlocked.Increment(ref _coldStarts);
            _platform.Note($"{id} started COLD: grid loaded from the database ({replica.Count} entries)");
        }

        var unit = new ProcessingUnit(id, _platform.Options.CapacityPerUnit, replica, _processing,
            _platform.Simulation, _platform.Messages);
        _platform.Messages.Add(unit);
        return unit;
    }

    /// <summary>No unit left: the first request starts one (only one, even if a thousand requests arrive).</summary>
    private async Task StartCold()
    {
        await _singleColdStart.WaitAsync();
        try
        {
            if (_platform.Messages.Units().Count == 0)
                await Start();
        }
        finally
        {
            _singleColdStart.Release();
        }
    }

    /// <summary>Clean stop: the unit receives nothing more, finishes its requests, then disappears. The data stays in the other replicas.</summary>
    public async Task Stop(ProcessingUnit unit)
    {
        _platform.Messages.Remove(unit);
        await unit.Drain();
        _platform.Data.RemoveReplica(unit.Id);
        _platform.Note($"{unit.Id} stopped ({unit.Processed} requests processed)");
    }

    /// <summary>Sudden failure: the unit and its memory disappear at once.</summary>
    public void Kill(ProcessingUnit unit)
    {
        unit.Kill();
        _platform.Messages.Remove(unit);
        _platform.Data.RemoveReplica(unit.Id);
        _platform.Note($"{unit.Id} FAILED: its replica is lost");
    }

    /// <summary>
    /// One evaluation: too loaded → one more unit; almost empty → one less. Called regularly (background service).
    /// </summary>
    public async Task<string?> Evaluate()
    {
        var units = _platform.Messages.Units();
        if (units.Count == 0)
            return null;
        var options = _platform.Options;
        var load = units.Average(u => u.Load);

        if (load >= options.HighThreshold && units.Count < options.MaxUnits)
        {
            var unit = await Start();
            return $"load {load * 100:0}%: {unit.Id} added";
        }
        if (load <= options.LowThreshold && units.Count > options.MinUnits)
        {
            var leastLoaded = units.MinBy(u => u.Load)!;
            await Stop(leastLoaded);
            return $"load {load * 100:0}%: {leastLoaded.Id} removed";
        }
        return null;
    }
}
