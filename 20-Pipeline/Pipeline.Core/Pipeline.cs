using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

namespace Pipeline.Core;

public enum RunStatus { Completed, Failed, Cancelled }

/// <summary>The report of a run.</summary>
public sealed record Report(
    RunStatus Status,
    string? Error,
    long Lines,
    long Outputs,
    long Rejections,
    IReadOnlyDictionary<string, long> RejectionsByReason,
    IReadOnlyList<Rejection> FirstRejections,
    long DurationMs,
    long MaxInFlight,
    IReadOnlyList<StageState> Stages)
{
    /// <summary>The busiest stage: it sets the throughput of the whole pipeline.</summary>
    public string? Bottleneck => Stages.Count == 0 ? null : Stages.MaxBy(e => e.Occupancy)!.Name;

    public long ThroughputPerSecond => DurationMs == 0 ? Lines : Lines * 1000 / DurationMs;
}

public static class Pipeline
{
    /// <summary>Starts a pipeline whose source produces <typeparamref name="TSource"/> items.</summary>
    public static Builder<TSource, TSource> From<TSource>(int pipeCapacity = 64) => new(pipeCapacity, []);
}

/// <summary>A pipeline is assembled filter by filter; the compiler checks that the output of one is the input of the next.</summary>
public sealed class Builder<TSource, TCurrent>
{
    private readonly int _capacity;
    private readonly IReadOnlyList<Stage> _stages;

    internal Builder(int capacity, IReadOnlyList<Stage> stages)
    {
        _capacity = capacity;
        _stages = stages;
    }

    /// <param name="parallelism">Number of copies of the filter working at the same time (stateless filter only).</param>
    /// <param name="ordered">With several copies: have the items come out in their arrival order.</param>
    public Builder<TSource, TNext> Then<TNext>(IFilter<TCurrent, TNext> filter, int parallelism = 1, bool ordered = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        return new Builder<TSource, TNext>(_capacity, [.. _stages, new Stage<TCurrent, TNext>(filter, parallelism, ordered)]);
    }

    public Pipeline<TSource, TCurrent> Build() => new(_capacity, _stages);
}

/// <summary>An assembled pipeline, reusable: each run creates its own pipes.</summary>
public sealed class Pipeline<TSource, TOutput>
{
    private readonly int _capacity;
    private readonly IReadOnlyList<Stage> _stages;

    internal Pipeline(int capacity, IReadOnlyList<Stage> stages)
    {
        _capacity = capacity;
        _stages = stages;
    }

    public IReadOnlyList<string> Filters => _stages.Select(e => e.Name).ToList();

    /// <summary>
    /// Passes the whole source through the pipeline, as a stream: the stages work AT THE SAME TIME, each on a different
    /// item, and never more than a few hundred items are in memory, whatever the size of the source.
    /// </summary>
    public async Task<Report> Run(IAsyncEnumerable<TSource> source, Action<TOutput>? sink = null, CancellationToken cancellation = default)
    {
        // Each run has its own counters: we work on copies of the stages.
        var stages = _stages.Select(e => e.Copy()).ToList();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var rejectionsByReason = new ConcurrentDictionary<string, long>();
        var firstRejections = new ConcurrentQueue<Rejection>();
        var context = new RunContext(stopping.Token, rejection =>
        {
            rejectionsByReason.AddOrUpdate(rejection.Reason, 1, (_, n) => n + 1);
            if (firstRejections.Count < 100)
                firstRejections.Enqueue(rejection);
        });
        var stopwatch = Stopwatch.StartNew();
        long lines = 0, outputs = 0;

        var sourcePipe = Channel.CreateBounded<Element<TSource>>(_capacity);
        var tasks = new List<Task>
        {
            Task.Run(async () =>
            {
                try
                {
                    await foreach (var value in source.WithCancellation(stopping.Token))
                    {
                        context.Enter();
                        await sourcePipe.Writer.WriteAsync(new Element<TSource>(++lines, value), stopping.Token);
                    }
                    sourcePipe.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    sourcePipe.Writer.TryComplete(e);
                    throw;
                }
            })
        };

        object pipe = sourcePipe.Reader;
        foreach (var stage in stages)
        {
            var (work, output) = stage.Launch(pipe, _capacity, context);
            tasks.Add(work);
            pipe = output;
        }

        tasks.Add(Task.Run(async () =>
        {
            await foreach (var element in ((ChannelReader<Element<TOutput>>)pipe).ReadAllAsync(stopping.Token))
            {
                sink?.Invoke(element.Value);
                outputs++;
                context.Exit();
            }
        }));

        // The first stage that fails stops all the others.
        foreach (var task in tasks)
            _ = task.ContinueWith(_ => stopping.Cancel(), TaskContinuationOptions.OnlyOnFaulted);

        var (status, error) = (RunStatus.Completed, (string?)null);
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            var cause = tasks.Where(t => t.IsFaulted).SelectMany(t => t.Exception!.InnerExceptions)
                .FirstOrDefault(e => e is not OperationCanceledException and not ChannelClosedException);
            (status, error) = cause is not null ? (RunStatus.Failed, cause.Message)
                : cancellation.IsCancellationRequested ? (RunStatus.Cancelled, "Run cancelled.")
                : (RunStatus.Failed, "A stage failed.");
        }
        stopwatch.Stop();

        return new Report(status, error, lines, outputs, rejectionsByReason.Values.Sum(),
            rejectionsByReason.OrderByDescending(r => r.Value).ToDictionary(r => r.Key, r => r.Value),
            firstRejections.OrderBy(r => r.Line).ToList(), stopwatch.ElapsedMilliseconds, context.MaxInFlight,
            stages.Select(e => e.State(stopwatch.Elapsed)).ToList());
    }
}
