namespace Sls.Platform;

public enum TriggerType { Http, Queue, Timer, Storage }

public sealed record FunctionDefinition(
    string Name,
    TriggerType Type,
    string Trigger,
    int MemoryMb,
    TimeSpan Timeout,
    Func<object, FunctionContext, Task<object?>> Execute)
{
    public string? Method { get; init; }
    public string? Route { get; init; }
    public string? Queue { get; init; }
    public string? Prefix { get; init; }
    public TimeSpan? Period { get; init; }
}

/// <summary>
/// The "deployment manifest": the list of functions and of what triggers them.
/// It is the equivalent of serverless.yml, template.yaml (AWS SAM) or Azure's [Function] attributes.
/// The allocated memory and the maximum duration are set per function, and determine the price.
/// </summary>
public sealed class Manifest
{
    internal List<FunctionDefinition> Functions { get; } = [];

    public Manifest Http(string name, string method, string route,
        Func<HttpRequestData, FunctionContext, Task<HttpResponseData>> function, int memoryMb = 128, double timeoutSeconds = 10) =>
        Add(new FunctionDefinition(name, TriggerType.Http, $"HTTP {method} {route}", memoryMb,
            TimeSpan.FromSeconds(timeoutSeconds), async (d, c) => await function((HttpRequestData)d, c))
        {
            Method = method, Route = route
        });

    public Manifest Queue(string name, string queue,
        Func<QueueMessage, FunctionContext, Task> function, int memoryMb = 128, double timeoutSeconds = 30) =>
        Add(new FunctionDefinition(name, TriggerType.Queue, $"Queue \"{queue}\"", memoryMb,
            TimeSpan.FromSeconds(timeoutSeconds), async (d, c) => { await function((QueueMessage)d, c); return null; })
        {
            Queue = queue
        });

    public Manifest Timer(string name, TimeSpan period,
        Func<TimerTick, FunctionContext, Task> function, int memoryMb = 128, double timeoutSeconds = 30) =>
        Add(new FunctionDefinition(name, TriggerType.Timer, $"Every {period.TotalSeconds:0} s", memoryMb,
            TimeSpan.FromSeconds(timeoutSeconds), async (d, c) => { await function((TimerTick)d, c); return null; })
        {
            Period = period
        });

    /// <summary>Triggered for each object written under a prefix (internally: through a queue, to benefit from the retries).</summary>
    public Manifest Storage(string name, string prefix,
        Func<StoredObject, FunctionContext, Task> function, int memoryMb = 128, double timeoutSeconds = 30) =>
        Add(new FunctionDefinition(name, TriggerType.Storage, $"Storage \"{prefix}*\"", memoryMb,
            TimeSpan.FromSeconds(timeoutSeconds), async (d, c) => { await function((StoredObject)d, c); return null; })
        {
            Prefix = prefix, Queue = $"$storage/{name}"
        });

    private Manifest Add(FunctionDefinition definition)
    {
        Functions.Add(definition);
        return this;
    }
}
