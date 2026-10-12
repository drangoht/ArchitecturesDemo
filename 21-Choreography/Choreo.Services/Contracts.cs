using Choreo.Bus;

namespace Choreo.Services.Contracts;

// The ONLY code shared between services: the shape of the events. Changing a contract means warning all its subscribers…
// whom the emitter does not know. (For real: versioned schemas, a schema registry.)

/// <summary>
/// The transfer data, copied into EACH event and completed along the way: each service finds what
/// it needs in the received event, without calling anyone back ("event-carried state transfer").
/// </summary>
public sealed record Order(Guid Source, Guid Target, decimal Amount)
{
    public string SourceCurrency { get; init; } = "";
    public string TargetCurrency { get; init; } = "";
    public decimal AmountEur { get; init; }
    public decimal TargetAmount { get; init; }
}

public abstract record TransferEvent(Order Order) : Event;

public sealed record TransferRequested(Order Order) : TransferEvent(Order);

public sealed record FundsReserved(Order Order) : TransferEvent(Order);
public sealed record FundsRefused(Order Order, string Reason) : TransferEvent(Order);

public sealed record ComplianceApproved(Order Order) : TransferEvent(Order);
public sealed record CompliancePending(Order Order) : TransferEvent(Order);
public sealed record ComplianceRefused(Order Order, string Reason) : TransferEvent(Order);

public sealed record CurrenciesConverted(Order Order) : TransferEvent(Order);
public sealed record ExchangeFailed(Order Order, string Reason) : TransferEvent(Order);

/// <summary>The point of no return: the target is credited, the source debited for good.</summary>
public sealed record TransferSettled(Order Order) : TransferEvent(Order);

/// <summary>Compensation done: the reservation is released after a refusal or a failure downstream.</summary>
public sealed record FundsReleased(Order Order, string Reason) : TransferEvent(Order);

/// <summary>Nobody concluded in time: the Accounts service released the reservation by itself.</summary>
public sealed record ReservationExpired(Order Order) : TransferEvent(Order);

/// <summary>A settlement arriving AFTER the expiry of the reservation: refused.</summary>
public sealed record TransferAbandoned(Order Order, string Reason) : TransferEvent(Order);

public sealed record CustomerNotified(Order Order, string Message) : TransferEvent(Order);
