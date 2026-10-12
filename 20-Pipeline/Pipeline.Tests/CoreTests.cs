using Pipeline.Core;

namespace Pipeline.Tests;

/// <summary>A test filter that takes its time (depending on the item) and notes how many filters work at the same time.</summary>
public sealed class Slow<T>(string name, Func<T, int> delayMs, Occupancy? occupancy = null) : IFilter<T, T>
{
    public string Name => name;

    public async ValueTask<Outcome<T>> Process(T input, CancellationToken cancellation)
    {
        occupancy?.Enter();
        try
        {
            await Task.Delay(delayMs(input), cancellation);
            return Outcome<T>.Ok(input);
        }
        finally
        {
            occupancy?.Exit();
        }
    }
}

public sealed class Occupancy
{
    private int _inProgress, _max;
    public int Max => Volatile.Read(ref _max);

    public void Enter()
    {
        var n = Interlocked.Increment(ref _inProgress);
        int max;
        while (n > (max = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, n, max) != max) { }
    }

    public void Exit() => Interlocked.Decrement(ref _inProgress);
}

/// <summary>The engine alone, with test filters: order, rejections, simultaneity, back-pressure, parallelism, failures.</summary>
public class CoreTests
{
    private static async IAsyncEnumerable<int> Numbers(int n)
    {
        for (var i = 1; i <= n; i++)
        {
            yield return i;
            if (i % 100 == 0)
                await Task.Yield();
        }
    }

    private static FunctionFilter<int, int> Double() => new("Double", x => Outcome<int>.Ok(x * 2));

    private static FunctionFilter<int, int> RejectMultiplesOf(int n) =>
        new($"Multiples of {n}", x => x % n == 0 ? Outcome<int>.Reject($"multiple of {n}", x.ToString()) : Outcome<int>.Ok(x));

    [Fact]
    public async Task The_items_go_through_the_filters_in_order()
    {
        var pipeline = Pipeline.Core.Pipeline.From<int>()
            .Then(Double())
            .Then(new FunctionFilter<int, string>("Format", x => Outcome<string>.Ok($"#{x}")))
            .Build();
        var outputs = new List<string>();

        var report = await pipeline.Run(Numbers(5), outputs.Add);

        Assert.Equal(["#2", "#4", "#6", "#8", "#10"], outputs);
        Assert.Equal((RunStatus.Completed, 5L, 5L), (report.Status, report.Lines, report.Outputs));
        Assert.Equal(["Double", "Format"], pipeline.Filters);
    }

    [Fact]
    public async Task A_rejection_discards_the_item_without_stopping_the_flow()
    {
        var pipeline = Pipeline.Core.Pipeline.From<int>().Then(RejectMultiplesOf(3)).Then(RejectMultiplesOf(5)).Build();
        var outputs = new List<int>();

        var report = await pipeline.Run(Numbers(15), outputs.Add);

        Assert.Equal([1, 2, 4, 7, 8, 11, 13, 14], outputs);
        Assert.Equal(7, report.Rejections);
        Assert.Equal(5, report.RejectionsByReason["multiple of 3"]);
        Assert.Equal(2, report.RejectionsByReason["multiple of 5"]);
        Assert.Equal(new Rejection(10, "Multiples of 5", "multiple of 5", "10"), report.FirstRejections.Single(r => r.Line == 10));
    }

    [Fact]
    public async Task The_stages_work_at_the_same_time_each_on_one_item()
    {
        var occupancy = new Occupancy();
        var pipeline = Pipeline.Core.Pipeline.From<int>()
            .Then(new Slow<int>("A", _ => 10, occupancy))
            .Then(new Slow<int>("B", _ => 10, occupancy))
            .Then(new Slow<int>("C", _ => 10, occupancy))
            .Build();

        await pipeline.Run(Numbers(20));

        Assert.Equal(3, occupancy.Max);  // A processes item 3 while B processes 2 and C 1
    }

    [Fact]
    public async Task Back_pressure_bounds_what_is_in_memory()
    {
        // A slow last stage: without bounded pipes, the whole flow would pile up in front of it.
        var pipeline = Pipeline.Core.Pipeline.From<int>(pipeCapacity: 8)
            .Then(Double())
            .Then(Double())
            .Then(new Slow<int>("Slow", x => x % 1_000 == 0 ? 20 : 0))
            .Build();

        var report = await pipeline.Run(Numbers(20_000));

        Assert.Equal(20_000, report.Outputs);
        // 4 pipes of 8 (source + 3 stages), one item in each stage, one held by the source, one by the sink: 37 at most.
        Assert.InRange(report.MaxInFlight, 1, 4 * 8 + 3 + 2);
        Assert.True(report.Stages[1].OutputWaitMs > 0, "the fast stages must have waited for the slow one");
    }

    [Fact]
    public async Task An_ordered_parallel_stage_returns_the_items_in_their_arrival_order()
    {
        var occupancy = new Occupancy();
        // The first item is very slow: the following copies finish before it.
        var slow = new Slow<int>("Slow", x => x == 1 ? 400 : 1, occupancy);
        var ordered = new List<int>();
        var unordered = new List<int>();

        await Pipeline.Core.Pipeline.From<int>().Then(slow, parallelism: 4).Then(RejectMultiplesOf(3)).Build()
            .Run(Numbers(30), ordered.Add);
        await Pipeline.Core.Pipeline.From<int>().Then(slow, parallelism: 4, ordered: false).Then(RejectMultiplesOf(3)).Build()
            .Run(Numbers(30), unordered.Add);

        var expected = Enumerable.Range(1, 30).Where(x => x % 3 != 0).ToList();
        Assert.Equal(expected, ordered);
        Assert.NotEqual(expected, unordered);
        Assert.Equal(expected, unordered.Order());  // the same items, not in the same order
        Assert.Equal(1, unordered.Last());
        Assert.Equal(4, occupancy.Max);
    }

    [Fact]
    public async Task An_exception_in_a_filter_stops_the_whole_pipeline()
    {
        var pipeline = Pipeline.Core.Pipeline.From<int>(pipeCapacity: 8)
            .Then(new FunctionFilter<int, int>("Fragile", x => x == 500 ? throw new InvalidOperationException("boom at 500") : Outcome<int>.Ok(x)))
            .Then(Double())
            .Build();

        var report = await pipeline.Run(Numbers(1_000_000)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RunStatus.Failed, report.Status);
        Assert.Equal("boom at 500", report.Error);
        Assert.InRange(report.Lines, 500, 600);  // the source stopped soon after: it did not read the million
    }

    [Fact]
    public async Task Cancellation_stops_an_endless_flow()
    {
        static async IAsyncEnumerable<int> Endless()
        {
            for (var i = 0; ; i++)
            {
                yield return i;
                await Task.Yield();
            }
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var report = await Pipeline.Core.Pipeline.From<int>().Then(Double()).Build()
            .Run(Endless(), cancellation: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RunStatus.Cancelled, report.Status);
        Assert.True(report.Lines > 0);
    }

    [Fact]
    public async Task The_bottleneck_is_the_busiest_stage_and_each_run_has_its_counters()
    {
        var pipeline = Pipeline.Core.Pipeline.From<int>().Then(Double()).Then(new Slow<int>("Slow", _ => 2)).Then(Double()).Build();

        var first = await pipeline.Run(Numbers(50));
        var second = await pipeline.Run(Numbers(20));

        Assert.Equal("Slow", first.Bottleneck);
        Assert.Equal(50, first.Stages[1].Inputs);
        Assert.Equal(20, second.Stages[1].Inputs);
    }
}
