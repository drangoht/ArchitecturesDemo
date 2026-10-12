using System.Text.Json;

namespace Cqrs.Events;

/// <summary>A fact that happened in the domain (always in the past tense).</summary>
public interface IDomainEvent
{
    DateTime Date { get; }
}

public record AccountOpened(Guid AccountId, string Owner, decimal InitialDeposit, DateTime Date) : IDomainEvent;

public record MoneyWithdrawn(Guid AccountId, decimal Amount, decimal NewBalance, DateTime Date) : IDomainEvent;

public record TransferMade(
    Guid TransferId, Guid SourceId, Guid TargetId, decimal Amount,
    decimal SourceBalance, decimal TargetBalance, DateTime Date) : IDomainEvent;

/// <summary>An event and its position in the stream (the sequence serves as a checkpoint for the projections).</summary>
public record RecordedEvent(long Sequence, IDomainEvent Event);

/// <summary>Read access to the event stream of the write side.</summary>
public interface IEventStream
{
    Task<IReadOnlyList<RecordedEvent>> ReadAfter(long sequence, int maximum);
    Task<long> LastSequence();
}

public static class EventCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> Types =
        new[] { typeof(AccountOpened), typeof(MoneyWithdrawn), typeof(TransferMade) }.ToDictionary(t => t.Name);

    public static string Serialize(IDomainEvent @event) =>
        JsonSerializer.Serialize(@event, @event.GetType(), Json);

    public static IDomainEvent Deserialize(string type, string json) =>
        (IDomainEvent)JsonSerializer.Deserialize(json, Types[type], Json)!;
}
