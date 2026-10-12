using Sls.Platform;

namespace Sls.Tests;

/// <summary>The behaviour of the "cloud provider", checked with test functions.</summary>
public class PlatformTests
{
    private static readonly PlatformOptions Options = new()
    {
        ColdStartMs = 30, MsBeforeRecycling = 100, MaxInstancesPerFunction = 3
    };

    private static ExecutionEngine Engine(Action<Manifest> deployment)
    {
        var manifest = new Manifest();
        deployment(manifest);
        return new ExecutionEngine(manifest, new ManagedServices(Options), Options);
    }

    private static HttpRequestData Request() => new("GET", "/", new Dictionary<string, string>(), "");

    private static Manifest Slow(Manifest m, int durationMs) =>
        m.Http("Slow", "GET", "/", async (_, _) => { await Task.Delay(durationMs); return HttpResponseData.Ok(null); });

    [Fact]
    public async Task The_first_call_starts_cold_the_next_ones_reuse_the_warm_instance()
    {
        var engine = Engine(m => Slow(m, 1));

        var first = await engine.Invoke("Slow", Request());
        var second = await engine.Invoke("Slow", Request());

        Assert.True(first.ColdStart);
        Assert.False(second.ColdStart);
        Assert.Equal(first.Instance, second.Instance);
    }

    [Fact]
    public async Task Simultaneous_calls_raise_the_number_of_instances()
    {
        var engine = Engine(m => Slow(m, 100));

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => engine.Invoke("Slow", Request())));

        Assert.Equal(3, results.Select(r => r.Instance).Distinct().Count());
        Assert.Equal(3, engine.Statistics().Single().PeakInstances);
    }

    [Fact]
    public async Task Beyond_the_maximum_of_instances_the_calls_are_throttled()
    {
        var engine = Engine(m => Slow(m, 200));

        var calls = Enumerable.Range(0, 4).Select(_ => engine.Invoke("Slow", Request())).ToList();

        await Assert.ThrowsAsync<ThrottlingException>(() => Task.WhenAll(calls));
        Assert.Equal(3, calls.Count(a => a.IsCompletedSuccessfully));
    }

    [Fact]
    public async Task Without_traffic_the_instances_are_recycled_and_their_memory_lost()
    {
        var engine = Engine(m => m.Http("Memory", "GET", "/", (_, context) =>
        {
            var calls = (context.InstanceMemory.TryGetValue("n", out var n) ? (int)n : 0) + 1;
            context.InstanceMemory["n"] = calls;
            return Task.FromResult(HttpResponseData.Ok(calls));
        }));

        await engine.Invoke("Memory", Request());
        var warm = await engine.Invoke("Memory", Request());
        await Task.Delay(150);
        engine.Recycle();
        var afterRecycling = await engine.Invoke("Memory", Request());

        Assert.Equal(2, ((HttpResponseData)warm.Value!).Body);
        Assert.True(afterRecycling.ColdStart);
        Assert.Equal(1, ((HttpResponseData)afterRecycling.Value!).Body);  // the memory of the old instance is gone
        Assert.Equal(1, engine.Statistics().Single().ActiveInstances);
    }

    [Fact]
    public async Task A_function_that_exceeds_its_timeout_is_stopped()
    {
        var engine = Engine(m => m.Http("Stuck", "GET", "/",
            async (_, _) => { await Task.Delay(2_000); return HttpResponseData.Ok(null); }, timeoutSeconds: 0.1));

        await Assert.ThrowsAsync<TimeoutExceededException>(() => engine.Invoke("Stuck", Request()));

        var statistics = engine.Statistics().Single();
        Assert.Equal(1, statistics.Errors);
        Assert.Equal(0, statistics.ActiveInstances);  // the faulty instance was destroyed
    }

    [Fact]
    public async Task You_pay_the_duration_multiplied_by_the_allocated_memory()
    {
        var engine = Engine(m => m.Http("Big", "GET", "/",
            async (_, _) => { await Task.Delay(100); return HttpResponseData.Ok(null); }, memoryMb: 1024));

        await engine.Invoke("Big", Request());

        // 1 GB for ~130 ms (cold start included) ≈ 0.13 GB-s
        Assert.InRange(engine.Statistics().Single().GbSeconds, 0.12, 0.5);
    }
}
