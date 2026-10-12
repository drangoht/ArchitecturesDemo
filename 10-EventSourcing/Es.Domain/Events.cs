namespace Es.Domain;

/// <summary>
/// A past, immutable fact. With Event Sourcing, the sequence of these facts IS the data:
/// there is no "Accounts" table with a balance. The balance is deduced from the events.
/// </summary>
public interface IEvent
{
    Guid AccountId { get; }
    DateTime Date { get; }
}

public record AccountOpened(Guid AccountId, string Owner, decimal InitialDeposit, DateTime Date) : IEvent;

public record MoneyDeposited(Guid AccountId, decimal Amount, DateTime Date) : IEvent;

/// <summary>
/// Version 2 of the event: the reason was added afterwards. The old events (v1, without a reason)
/// stay in the store as they are; they are converted when read ("upcasting", see Es.Storage).
/// </summary>
public record MoneyWithdrawn(Guid AccountId, decimal Amount, string Reason, DateTime Date) : IEvent;

public record TransferSent(Guid AccountId, Guid TransferId, Guid To, decimal Amount, DateTime Date) : IEvent;

public record TransferReceived(Guid AccountId, Guid TransferId, Guid From, decimal Amount, DateTime Date) : IEvent;

public record AccountClosed(Guid AccountId, DateTime Date) : IEvent;
