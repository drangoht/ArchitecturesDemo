namespace Fig.Modern;

public sealed record SyncReport(int LinesRead, int FactsApplied, int Forwarded, long LegacyPosition);

/// <summary>
/// As long as the legacy system lives, both databases must say the same thing:
/// ① what the legacy system writes (including through batches that do not go through the facade) is caught up here;
/// ② what the modern service writes is forwarded to the legacy system, for its routes not yet migrated… and to be able to
///    roll back at any time.
/// </summary>
public sealed class Synchronization(Store store, LegacyClient legacy, ILogger<Synchronization> logger)
{
    private const int BatchSize = 100;
    private readonly SemaphoreSlim _singleCycle = new(1, 1);

    public async Task<SyncReport> Run(CancellationToken cancellation = default)
    {
        await _singleCycle.WaitAsync(cancellation);
        try
        {
            // ① Catch up with the legacy system, from the last known position (the first cycle reloads everything from the start).
            int read = 0, applied = 0;
            while (true)
            {
                var rows = await legacy.Log(store.LegacyPosition, BatchSize, cancellation);
                if (rows.Count == 0)
                    break;
                var facts = rows.Select(LegacyTranslator.Translate).ToList();
                applied += store.ApplyFromLegacy(facts, rows[^1].Position);
                read += rows.Count;
                if (rows.Count < BatchSize)
                    break;
            }

            // ② Forward our writes. If the legacy system does not answer, they stay in the outbox for the next
            //    cycle; the import being idempotent, a batch sent again is not counted twice.
            var toForward = store.ToForward();
            if (toForward.Count > 0)
            {
                await legacy.Import(toForward.Select(LegacyTranslator.ToLegacy).ToList(), cancellation);
                store.MarkForwarded(toForward);
            }

            if (read > 0 || toForward.Count > 0)
                logger.LogInformation("Synchronization: {Read} rows read from the legacy system, {Applied} facts applied, {Forwarded} operations forwarded",
                    read, applied, toForward.Count);
            return new SyncReport(read, applied, toForward.Count, store.LegacyPosition);
        }
        finally
        {
            _singleCycle.Release();
        }
    }
}

/// <summary>A synchronization cycle every <c>Synchronization:IntervalMs</c> (0: only on demand, for the tests).</summary>
public sealed class PeriodicSynchronization(Synchronization synchronization, IConfiguration configuration,
    ILogger<PeriodicSynchronization> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var interval = configuration.GetValue("Synchronization:IntervalMs", 500);
        if (interval <= 0)
            return;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await synchronization.Run(stopping);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !stopping.IsCancellationRequested)
            {
                logger.LogWarning("Legacy system unreachable, new attempt in {Interval} ms", interval);
            }
            catch (FormatException e)
            {
                logger.LogError(e, "Synchronization stopped on an unreadable row");
            }
            await Task.Delay(interval, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
