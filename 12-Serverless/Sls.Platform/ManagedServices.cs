using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sls.Platform;

public class PlatformOptions
{
    public int ColdStartMs { get; set; } = 600;
    public int MsBeforeRecycling { get; set; } = 20_000;
    public int MaxInstancesPerFunction { get; set; } = 10;
    public int MaxAttempts { get; set; } = 3;
    public int DelayBetweenAttemptsMs { get; set; } = 500;
}

/// <summary>
/// The provider's "managed" services: the function has no disk and no durable memory,
/// everything that must survive an invocation goes into these services.
/// </summary>
public sealed class ManagedServices
{
    private readonly ConcurrentDictionary<string, ManagedTable> _tables = new();

    public ManagedServices(PlatformOptions options, IEnumerable<KeyValuePair<string, string?>>? settings = null, ILogger? logger = null)
    {
        Log = new CentralLog(logger);
        Queues = new QueueService(options);
        Storage = new StorageService(Queues);
        foreach (var (key, value) in settings ?? [])
            if (value is not null)
                Settings[key] = value;
    }

    public CentralLog Log { get; }
    public QueueService Queues { get; }
    public StorageService Storage { get; }

    /// <summary>Application settings (environment variables of the functions), changeable without redeploying.</summary>
    public ConcurrentDictionary<string, string> Settings { get; } = new();

    public ManagedTable Table(string name) => _tables.GetOrAdd(name, n => new ManagedTable(n));
}

// ─── Key-value database (DynamoDB / Azure Table Storage style) ───

public record StoredEntity(string Key, string Data, long ETag);

/// <summary>
/// Each write increments the entity's ETag. A conditional write ("only if the ETag is still n")
/// lets dozens of parallel instances modify the same data without overwriting each other.
/// </summary>
public sealed class ManagedTable(string name)
{
    private readonly Dictionary<string, StoredEntity> _entities = [];
    private readonly Lock _lock = new();

    public string Name { get; } = name;

    public Task<(T? Value, long ETag)> Read<T>(string key)
    {
        lock (_lock)
            return Task.FromResult(_entities.TryGetValue(key, out var e)
                ? (JsonSerializer.Deserialize<T>(e.Data, Json.Options), e.ETag)
                : (default(T), 0L));
    }

    /// <param name="expectedEtag">null: unconditional write; 0: the entity must not exist; n: it must still be at ETag n.</param>
    public Task<long> Write<T>(string key, T value, long? expectedEtag = null)
    {
        lock (_lock)
        {
            var current = _entities.TryGetValue(key, out var e) ? e.ETag : 0;
            if (expectedEtag is not null && expectedEtag != current)
                throw new WriteConflictException(Name, key);
            _entities[key] = new StoredEntity(key, JsonSerializer.Serialize(value, Json.Options), current + 1);
            return Task.FromResult(current + 1);
        }
    }

    public Task<IReadOnlyList<T>> List<T>(string prefix = "")
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<T>>(_entities.Values
                .Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(e => e.Key)
                .Select(e => JsonSerializer.Deserialize<T>(e.Data, Json.Options)!)
                .ToList());
    }
}

// ─── Message queues (SQS / Azure Queue Storage style) ───

public record QueuedMessage(string Id, string Body, int Attempt, DateTime VisibleAfter, string? LastError);

/// <summary>
/// "At least once" delivery: a failed message is redelivered after an increasing wait;
/// after N failures, it goes to the "-poison" queue (the poison messages), where it waits for a human to take care of it.
/// </summary>
public sealed class QueueService(PlatformOptions options)
{
    private readonly Dictionary<string, List<QueuedMessage>> _queues = [];
    private readonly Lock _lock = new();

    public Task Send(string queue, string body)
    {
        Add(queue, new QueuedMessage(Guid.NewGuid().ToString("N"), body, 0, DateTime.UtcNow, null));
        return Task.CompletedTask;
    }

    internal QueuedMessage? Take(string queue)
    {
        lock (_lock)
        {
            if (!_queues.TryGetValue(queue, out var messages))
                return null;
            var message = messages.FirstOrDefault(m => m.VisibleAfter <= DateTime.UtcNow);
            if (message is not null)
                messages.Remove(message);
            return message;
        }
    }

    /// <summary>The invocation failed: retry later, or poison message queue.</summary>
    internal void Failed(string queue, QueuedMessage message, string error)
    {
        var attempt = message.Attempt + 1;
        if (attempt >= options.MaxAttempts)
            Add($"{queue}-poison", message with { Attempt = attempt, LastError = error });
        else
            Add(queue, message with
            {
                Attempt = attempt,
                LastError = error,
                VisibleAfter = DateTime.UtcNow.AddMilliseconds(options.DelayBetweenAttemptsMs * Math.Pow(2, attempt - 1))
            });
    }

    /// <summary>No instance available: the message is put back in the queue without counting an attempt.</summary>
    internal void PutBack(string queue, QueuedMessage message) =>
        Add(queue, message with { VisibleAfter = DateTime.UtcNow.AddMilliseconds(100) });

    /// <summary>Sends the poison messages back to their original queue (after the problem is fixed).</summary>
    public int Replay(string poisonQueue)
    {
        List<QueuedMessage> messages;
        lock (_lock)
        {
            if (!_queues.Remove(poisonQueue, out messages!))
                return 0;
        }
        var origin = poisonQueue.EndsWith("-poison") ? poisonQueue[..^"-poison".Length] : poisonQueue;
        foreach (var message in messages)
            Add(origin, message with { Attempt = 0, VisibleAfter = DateTime.UtcNow });
        return messages.Count;
    }

    public IReadOnlyDictionary<string, object> State()
    {
        lock (_lock)
            return _queues.Where(f => f.Value.Count > 0).ToDictionary(f => f.Key, f => (object)new
            {
                messages = f.Value.Count,
                latestErrors = f.Value.Where(m => m.LastError is not null).Select(m => m.LastError).Distinct().Take(3)
            });
    }

    private void Add(string queue, QueuedMessage message)
    {
        lock (_lock)
        {
            if (!_queues.TryGetValue(queue, out var messages))
                _queues[queue] = messages = [];
            messages.Add(message);
        }
    }
}

// ─── Object storage (S3 / Azure Blob Storage style) ───

public sealed class StorageService(QueueService queues)
{
    private readonly ConcurrentDictionary<string, string> _objects = new();
    private readonly List<(string Prefix, string Queue)> _subscriptions = [];

    internal void Subscribe(string prefix, string queue) => _subscriptions.Add((prefix, queue));

    public async Task Write(string path, string content)
    {
        _objects[path] = content;
        foreach (var (prefix, queue) in _subscriptions.Where(a => path.StartsWith(a.Prefix, StringComparison.Ordinal)))
            await queues.Send(queue, path);
    }

    public string? Read(string path) => _objects.GetValueOrDefault(path);

    public IReadOnlyList<string> List(string prefix = "") =>
        _objects.Keys.Where(c => c.StartsWith(prefix, StringComparison.Ordinal)).Order().ToList();
}

// ─── Centralized log (CloudWatch / Application Insights style) ───

public sealed class CentralLog(ILogger? logger)
{
    private readonly ConcurrentQueue<string> _lines = new();

    public void Write(string source, string message)
    {
        _lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff} [{source}] {message}");
        while (_lines.Count > 500)
            _lines.TryDequeue(out _);
        logger?.LogInformation("[{Source}] {Message}", source, message);
    }

    public IReadOnlyList<string> Latest(int count) => _lines.Reverse().Take(count).Reverse().ToList();
}
