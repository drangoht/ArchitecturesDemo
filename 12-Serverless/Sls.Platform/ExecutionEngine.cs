using System.Diagnostics;

namespace Sls.Platform;

public record InvocationResult(object? Value, bool ColdStart, string Instance, long DurationMs);

public record FunctionStatistics(
    string Name, string Trigger, int MemoryMb, long Invocations, long Errors, long ColdStarts,
    int ActiveInstances, int PeakInstances, double AverageDurationMs, double GbSeconds);

/// <summary>
/// The heart of the provider: it runs each invocation in an INSTANCE (an ephemeral container).
/// - A free and "warm" instance is reused; otherwise one is created: that is the cold start.
/// - An instance serves only one invocation at a time: 10 simultaneous requests = 10 instances (scaling out).
/// - An idle instance is recycled: without traffic, nothing is left running (scaling to zero).
/// - The execution time × the allocated memory is billed, by the millisecond.
/// </summary>
public sealed class ExecutionEngine
{
    // Public AWS Lambda prices (x86): $0.20 per million requests and $0.0000166667 per GB-second.
    public const decimal PricePerMillionRequests = 0.20m;
    public const decimal PricePerGbSecond = 0.0000166667m;

    private sealed class Instance(string id)
    {
        public string Id { get; } = id;
        public bool Busy { get; set; }
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Memory { get; } = [];
    }

    private sealed class FunctionState(FunctionDefinition definition)
    {
        public FunctionDefinition Definition { get; } = definition;
        public List<Instance> Instances { get; } = [];
        public int InstanceNumber;
        public long Invocations, Errors, ColdStarts;
        public double TotalDurationMs, GbSeconds;
        public int PeakInstances;
    }

    private readonly Dictionary<string, FunctionState> _functions;
    private readonly ManagedServices _services;
    private readonly PlatformOptions _options;

    public ExecutionEngine(Manifest manifest, ManagedServices services, PlatformOptions options)
    {
        _functions = manifest.Functions.ToDictionary(f => f.Name, f => new FunctionState(f));
        _services = services;
        _options = options;
        foreach (var function in manifest.Functions.Where(f => f.Type == TriggerType.Storage))
            services.Storage.Subscribe(function.Prefix!, function.Queue!);
    }

    public IEnumerable<FunctionDefinition> Definitions => _functions.Values.Select(e => e.Definition);

    public bool CanStart(string name)
    {
        var state = _functions[name];
        lock (state)
            return state.Instances.Any(i => !i.Busy) || state.Instances.Count < _options.MaxInstancesPerFunction;
    }

    public async Task<InvocationResult> Invoke(string name, object trigger)
    {
        var state = _functions[name];
        var definition = state.Definition;

        Instance? instance;
        bool cold;
        lock (state)
        {
            instance = state.Instances.FirstOrDefault(i => !i.Busy);
            cold = instance is null;
            if (instance is null)
            {
                if (state.Instances.Count >= _options.MaxInstancesPerFunction)
                    throw new ThrottlingException(name);
                instance = new Instance($"{++state.InstanceNumber}");
                state.Instances.Add(instance);
                state.PeakInstances = Math.Max(state.PeakInstances, state.Instances.Count);
            }
            instance.Busy = true;
        }

        var stopwatch = Stopwatch.StartNew();
        var (success, killed) = (false, false);
        try
        {
            if (cold)
            {
                // Downloading the code, starting the .NET runtime, initializing: that is the "cold start".
                _services.Log.Write($"{name}#{instance.Id}", "cold start of a new instance");
                await Task.Delay(_options.ColdStartMs);
            }

            using var cancellation = new CancellationTokenSource(definition.Timeout);
            var context = new FunctionContext(name, instance.Id, cold, instance.Memory, _services, cancellation.Token);
            var execution = definition.Execute(trigger, context);
            if (await Task.WhenAny(execution, Task.Delay(definition.Timeout)) != execution)
            {
                killed = true;
                throw new TimeoutExceededException(name, definition.Timeout);
            }

            var value = await execution;
            success = true;
            return new InvocationResult(value, cold, instance.Id, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            var durationMs = Math.Max(1, stopwatch.Elapsed.TotalMilliseconds);  // billed by the ms, cold start included
            lock (state)
            {
                state.Invocations++;
                state.ColdStarts += cold ? 1 : 0;
                state.Errors += success ? 0 : 1;
                state.TotalDurationMs += durationMs;
                state.GbSeconds += definition.MemoryMb / 1024.0 * durationMs / 1000.0;

                if (killed)
                    state.Instances.Remove(instance);  // an instance that exceeds its timeout is destroyed
                else
                    (instance.Busy, instance.LastUsed) = (false, DateTime.UtcNow);
            }
        }
    }

    /// <summary>Removes the instances idle for too long: the function goes back down to zero.</summary>
    public void Recycle()
    {
        var limit = DateTime.UtcNow.AddMilliseconds(-_options.MsBeforeRecycling);
        foreach (var state in _functions.Values)
        {
            lock (state)
            {
                foreach (var instance in state.Instances.Where(i => !i.Busy && i.LastUsed < limit).ToList())
                {
                    state.Instances.Remove(instance);
                    _services.Log.Write($"{state.Definition.Name}#{instance.Id}", "idle instance recycled");
                }
            }
        }
    }

    public IReadOnlyList<FunctionStatistics> Statistics() => _functions.Values.Select(e =>
    {
        lock (e)
            return new FunctionStatistics(e.Definition.Name, e.Definition.Trigger, e.Definition.MemoryMb,
                e.Invocations, e.Errors, e.ColdStarts, e.Instances.Count, e.PeakInstances,
                e.Invocations == 0 ? 0 : Math.Round(e.TotalDurationMs / e.Invocations, 1), Math.Round(e.GbSeconds, 4));
    }).ToList();

    public object Bill()
    {
        var statistics = Statistics();
        var invocations = statistics.Sum(s => s.Invocations);
        var gbSeconds = statistics.Sum(s => s.GbSeconds);
        var requestCost = invocations / 1_000_000m * PricePerMillionRequests;
        var computeCost = (decimal)gbSeconds * PricePerGbSecond;
        return new
        {
            invocations,
            gbSeconds = Math.Round(gbSeconds, 4),
            requestCostUsd = Math.Round(requestCost, 8),
            computeCostUsd = Math.Round(computeCost, 8),
            totalUsd = Math.Round(requestCost + computeCost, 8),
            note = "AWS Lambda prices, excluding the free tier (1 million requests and 400,000 GB-s per month). " +
                   "Nothing is billed when no function is running."
        };
    }
}
