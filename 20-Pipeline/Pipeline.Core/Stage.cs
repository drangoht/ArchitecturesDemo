using System.Diagnostics;
using System.Threading.Channels;

namespace Pipeline.Core;

/// <summary>What a stage did, measured during the run.</summary>
public sealed record StageState(
    string Name,
    int Parallelism,
    long Inputs,
    long Outputs,
    long Rejections,
    long ProcessingMs,
    long OutputWaitMs,
    double Occupancy);

/// <summary>Shared by all the stages of a run: rejections, items in flight, cancellation.</summary>
internal sealed class RunContext(CancellationToken cancellation, Action<Rejection> reject)
{
    private long _inFlight, _maxInFlight;

    public CancellationToken Cancellation { get; } = cancellation;

    public long MaxInFlight => Interlocked.Read(ref _maxInFlight);

    public void Enter()
    {
        var n = Interlocked.Increment(ref _inFlight);
        long max;
        while (n > (max = Interlocked.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, n, max) != max) { }
    }

    public void Exit() => Interlocked.Decrement(ref _inFlight);

    public void Reject(Rejection rejection)
    {
        reject(rejection);
        Exit();
    }
}

/// <summary>A stage of the pipeline: a filter, its output pipe, and the number of copies of the filter working at the same time.</summary>
internal abstract class Stage(string name, int parallelism, bool ordered)
{
    protected long Inputs, Outputs, Rejections, ProcessingTicks, OutputWaitTicks;

    public string Name { get; } = name;
    public int Parallelism { get; } = parallelism;
    public bool Ordered { get; } = ordered;

    /// <summary>Starts the stage: reads the input pipe, returns the output pipe (bounded: that is the back-pressure).</summary>
    public abstract (Task Work, object Output) Launch(object input, int capacity, RunContext context);

    /// <summary>The same stage, counters at zero (each run measures its own; the filter, however, is shared).</summary>
    public abstract Stage Copy();

    public StageState State(TimeSpan duration)
    {
        var processing = TimeSpan.FromTicks(Interlocked.Read(ref ProcessingTicks));
        var occupancy = duration > TimeSpan.Zero ? processing / (duration * Parallelism) : 0;
        return new StageState(Name, Parallelism, Interlocked.Read(ref Inputs), Interlocked.Read(ref Outputs), Interlocked.Read(ref Rejections),
            (long)processing.TotalMilliseconds, (long)TimeSpan.FromTicks(Interlocked.Read(ref OutputWaitTicks)).TotalMilliseconds,
            Math.Round(Math.Min(occupancy, 1), 3));
    }
}

internal sealed class Stage<TInput, TOutput>(IFilter<TInput, TOutput> filter, int parallelism, bool ordered)
    : Stage(filter.Name, parallelism, ordered)
{
    public override Stage Copy() => new Stage<TInput, TOutput>(filter, Parallelism, Ordered);

    public override (Task Work, object Output) Launch(object input, int capacity, RunContext context)
    {
        var output = Channel.CreateBounded<Element<TOutput>>(capacity);
        var reader = (ChannelReader<Element<TInput>>)input;
        var work = Parallelism == 1 ? Sequential(reader, output.Writer, context)
            : Ordered ? ParallelOrdered(reader, output.Writer, context)
            : ParallelUnordered(reader, output.Writer, context);
        return (Complete(work, output.Writer), output.Reader);
    }

    /// <summary>When the stage finishes (or fails), its output pipe is closed: the next stage will know it.</summary>
    private static async Task Complete(Task work, ChannelWriter<Element<TOutput>> output)
    {
        try
        {
            await work;
            output.TryComplete();
        }
        catch (Exception e)
        {
            output.TryComplete(e);
            throw;
        }
    }

    private async Task Sequential(ChannelReader<Element<TInput>> input, ChannelWriter<Element<TOutput>> output, RunContext context)
    {
        await foreach (var element in input.ReadAllAsync(context.Cancellation))
            if (await Process(element, context) is { } result)
                await Write(output, result, context);
    }

    /// <summary>N copies of the filter read the same pipe: the items come out in the order in which they FINISH.</summary>
    private Task ParallelUnordered(ChannelReader<Element<TInput>> input, ChannelWriter<Element<TOutput>> output, RunContext context) =>
        Task.WhenAll(Enumerable.Range(0, Parallelism).Select(_ => Task.Run(() => Sequential(input, output, context))));

    /// <summary>
    /// N copies of the filter, but the items come out in their ARRIVAL order: each one gets a number, and a
    /// "resequencer" holds back those that finish early. A window bounds what can be in progress at once.
    /// </summary>
    private async Task ParallelOrdered(ChannelReader<Element<TInput>> input, ChannelWriter<Element<TOutput>> output, RunContext context)
    {
        var window = new SemaphoreSlim(Parallelism * 4);
        var toProcess = Channel.CreateBounded<(long Rank, Element<TInput> Element)>(Parallelism);
        var processed = Channel.CreateUnbounded<(long Rank, Element<TOutput>? Result)>();

        var distribution = Task.Run(async () =>
        {
            try
            {
                long rank = 0;
                await foreach (var element in input.ReadAllAsync(context.Cancellation))
                {
                    await window.WaitAsync(context.Cancellation);
                    await toProcess.Writer.WriteAsync((rank++, element), context.Cancellation);
                }
            }
            finally
            {
                toProcess.Writer.TryComplete();
            }
        });

        var copies = Task.WhenAll(Enumerable.Range(0, Parallelism).Select(_ => Task.Run(async () =>
        {
            await foreach (var (rank, element) in toProcess.Reader.ReadAllAsync(context.Cancellation))
                await processed.Writer.WriteAsync((rank, await Process(element, context)), context.Cancellation);
        })));
        var end = copies.ContinueWith(t => processed.Writer.TryComplete(t.Exception?.InnerException), TaskScheduler.Default);

        var early = new Dictionary<long, Element<TOutput>?>();
        long expected = 0;
        await foreach (var (rank, result) in processed.Reader.ReadAllAsync(context.Cancellation))
        {
            early[rank] = result;
            while (early.Remove(expected, out var next))
            {
                if (next is not null)
                    await Write(output, next, context);
                expected++;
                window.Release();
            }
        }
        await Task.WhenAll(distribution, copies, end);
    }

    /// <summary>Calls the filter and counts. Returns null if the item is rejected.</summary>
    private async Task<Element<TOutput>?> Process(Element<TInput> element, RunContext context)
    {
        Interlocked.Increment(ref Inputs);
        var start = Stopwatch.GetTimestamp();
        var outcome = await filter.Process(element.Value, context.Cancellation);
        Interlocked.Add(ref ProcessingTicks, Stopwatch.GetElapsedTime(start).Ticks);

        if (outcome.IsRejection)
        {
            Interlocked.Increment(ref Rejections);
            context.Reject(new Rejection(element.Line, Name, outcome.Reason!, outcome.Detail));
            return null;
        }
        Interlocked.Increment(ref Outputs);
        return new Element<TOutput>(element.Line, outcome.Value!);
    }

    /// <summary>Writing into a full pipe BLOCKS: the stage waits for the next one to have room. That is the back-pressure.</summary>
    private async Task Write(ChannelWriter<Element<TOutput>> output, Element<TOutput> element, RunContext context)
    {
        var start = Stopwatch.GetTimestamp();
        await output.WriteAsync(element, context.Cancellation);
        Interlocked.Add(ref OutputWaitTicks, Stopwatch.GetElapsedTime(start).Ticks);
    }
}
