using System.Text.Json;

namespace Sls.Platform;

/// <summary>
/// What the platform gives a function for one invocation: information about the execution
/// and access to the managed services. It is AWS Lambda's "context" or Azure's FunctionContext.
/// </summary>
public sealed class FunctionContext
{
    private readonly ManagedServices _services;

    internal FunctionContext(string functionName, string instanceId, bool coldStart,
        IDictionary<string, object> instanceMemory, ManagedServices services, CancellationToken cancellation)
    {
        (FunctionName, InstanceId, ColdStart, InstanceMemory, _services, Cancellation) =
            (functionName, instanceId, coldStart, instanceMemory, services, cancellation);
    }

    /// <summary>To test a function directly, without a platform: it is a mere method.</summary>
    public static FunctionContext ForTests(ManagedServices services, IDictionary<string, object>? instanceMemory = null) =>
        new("test", "test", false, instanceMemory ?? new Dictionary<string, object>(), services, CancellationToken.None);

    public string FunctionName { get; }
    public string InstanceId { get; }
    public bool ColdStart { get; }
    public Guid InvocationId { get; } = Guid.NewGuid();

    /// <summary>Cancelled when the maximum duration of the function is reached.</summary>
    public CancellationToken Cancellation { get; }

    /// <summary>
    /// Memory of the INSTANCE: kept between two invocations served by the same "warm" instance,
    /// lost at recycling, and different from one instance to another. Useful for a cache, never for data.
    /// </summary>
    public IDictionary<string, object> InstanceMemory { get; }

    public ManagedTable Table(string name) => _services.Table(name);

    public Task SendToQueue(string queue, object message) =>
        _services.Queues.Send(queue, JsonSerializer.Serialize(message, Json.Options));

    public Task WriteObject(string path, string content) => _services.Storage.Write(path, content);

    public string? Setting(string key) => _services.Settings.GetValueOrDefault(key);

    public void Log(string message) => _services.Log.Write($"{FunctionName}#{InstanceId}", message);
}
