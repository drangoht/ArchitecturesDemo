using System.Text.Encodings.Web;
using System.Text.Json;
using Es.Domain;

namespace Es.Storage;

/// <summary>An event as it is stored: its global position, its stream (the account) and its version in that stream.</summary>
public record StoredEvent(long Position, Guid Stream, int Version, string Type, string Data, DateTime Date)
{
    public IEvent Event() => EventSerializer.Deserialize(Type, Data);
}

/// <summary>Events to append to a stream, provided the stream is still at the expected version.</summary>
public record ToWrite(Guid Stream, int ExpectedVersion, IReadOnlyList<IEvent> Events);

public class ConcurrencyConflictException(Guid stream, int expectedVersion, int currentVersion)
    : Exception($"Account {stream} was changed in the meantime (expected version {expectedVersion}, current version {currentVersion}).");

public interface IEventStore
{
    Task<IReadOnlyList<StoredEvent>> ReadStream(Guid stream, int afterVersion = 0);
    Task<IReadOnlyList<StoredEvent>> ReadAll(long afterPosition = 0, int maximum = int.MaxValue);
    Task Append(params ToWrite[] writes);
}

/// <summary>
/// Append-only store: nothing is ever modified or deleted in it.
/// With a file, each event is appended to it as one JSON line; on restart, everything is read again.
/// </summary>
public sealed class EventStore : IEventStore
{
    private readonly List<StoredEvent> _events = [];
    private readonly string? _file;
    private readonly Lock _lock = new();

    // Format of a line of the file: the event's data is written as readable JSON, not as an escaped string.
    private record Line(long Position, Guid Stream, int Version, string Type, JsonElement Data, DateTime Date);

    private static readonly JsonSerializerOptions FileFormat = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public EventStore(string? file = null)
    {
        _file = file;
        if (file is not null && File.Exists(file))
            _events.AddRange(File.ReadLines(file)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<Line>(line, FileFormat)!)
                .Select(l => new StoredEvent(l.Position, l.Stream, l.Version, l.Type, l.Data.GetRawText(), l.Date)));
    }

    private static string ToLine(StoredEvent e) => JsonSerializer.Serialize(
        new Line(e.Position, e.Stream, e.Version, e.Type, JsonSerializer.Deserialize<JsonElement>(e.Data), e.Date),
        FileFormat);

    public Task<IReadOnlyList<StoredEvent>> ReadStream(Guid stream, int afterVersion = 0)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<StoredEvent>>(
                _events.Where(e => e.Stream == stream && e.Version > afterVersion).ToList());
    }

    public Task<IReadOnlyList<StoredEvent>> ReadAll(long afterPosition = 0, int maximum = int.MaxValue)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<StoredEvent>>(
                _events.Where(e => e.Position > afterPosition).Take(maximum).ToList());
    }

    /// <summary>
    /// Appends the events of one or more streams, all or nothing.
    /// Optimistic concurrency: if a stream moved since it was loaded, nothing is written.
    /// </summary>
    public Task Append(params ToWrite[] writes)
    {
        lock (_lock)
        {
            foreach (var write in writes)
            {
                var current = _events.Count(e => e.Stream == write.Stream);
                if (current != write.ExpectedVersion)
                    throw new ConcurrencyConflictException(write.Stream, write.ExpectedVersion, current);
            }

            var newEvents = new List<StoredEvent>();
            foreach (var write in writes)
            {
                var version = write.ExpectedVersion;
                foreach (var @event in write.Events)
                    newEvents.Add(new StoredEvent(
                        _events.Count + newEvents.Count + 1, write.Stream, ++version,
                        EventSerializer.TypeName(@event), EventSerializer.Serialize(@event), @event.Date));
            }

            if (_file is not null)
                File.AppendAllLines(_file, newEvents.Select(ToLine));
            _events.AddRange(newEvents);
        }
        return Task.CompletedTask;
    }
}
