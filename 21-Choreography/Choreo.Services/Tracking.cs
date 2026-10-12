using Choreo.Bus;
using Choreo.Services.Contracts;

namespace Choreo.Services.Tracking;

public enum TransferStatus { InProgress, AwaitingAnalyst, CancellationInProgress, Settled, Refused, Cancelled, Abandoned }

public sealed record TransferState(
    string Id,
    Order Order,
    TransferStatus Status,
    string LastStep,
    DateTimeOffset LastActivity,
    string? Reason,
    bool CustomerNotified)
{
    public bool IsFinished => Status is TransferStatus.Settled or TransferStatus.Refused or TransferStatus.Cancelled or TransferStatus.Abandoned;
}

/// <summary>
/// In choreography, nobody knows "where transfer 42 stands". This service rebuilds it by listening to ALL the
/// events of the process (a projection, as in CQRS). It decides nothing; it observes. Downside: it must know
/// each event type, and each new step of the process must be added to it.
/// </summary>
public sealed class TrackingService(Choreo.Bus.Bus bus) : Reactive(bus, "Tracking")
{
    private readonly Dictionary<string, TransferState> _transfers = new();

    public void Register()
    {
        Listen<TransferRequested>(TransferStatus.InProgress);
        Listen<FundsReserved>(TransferStatus.InProgress);
        Listen<FundsRefused>(TransferStatus.Refused, e => e.Reason);
        Listen<CompliancePending>(TransferStatus.AwaitingAnalyst);
        Listen<ComplianceApproved>(TransferStatus.InProgress);
        Listen<ComplianceRefused>(TransferStatus.CancellationInProgress, e => e.Reason);
        Listen<CurrenciesConverted>(TransferStatus.InProgress);
        Listen<ExchangeFailed>(TransferStatus.CancellationInProgress, e => e.Reason);
        Listen<FundsReleased>(TransferStatus.Cancelled, e => e.Reason);
        Listen<TransferSettled>(TransferStatus.Settled);
        Listen<ReservationExpired>(TransferStatus.Abandoned, _ => "reservation expired: nobody concluded in time");
        Listen<TransferAbandoned>(TransferStatus.Abandoned, e => e.Reason);
        Bus.Subscribe<CustomerNotified>(Name, e => Once(e, () =>
        {
            if (_transfers.TryGetValue(e.Correlation, out var v))
                _transfers[e.Correlation] = v with { CustomerNotified = true, LastActivity = e.Date };
            return null;
        }));
    }

    private void Listen<T>(TransferStatus status, Func<T, string>? reason = null) where T : TransferEvent =>
        Bus.Subscribe<T>(Name, e => Once(e, () =>
        {
            var previous = _transfers.GetValueOrDefault(e.Correlation);
            // A finished transfer stays finished: a late event (a settlement after expiry) does not reopen it.
            var newStatus = previous is { IsFinished: true } && status != TransferStatus.Abandoned ? previous.Status : status;
            _transfers[e.Correlation] = new TransferState(e.Correlation, e.Order, newStatus, e.Type, e.Date,
                reason?.Invoke(e) ?? previous?.Reason, previous?.CustomerNotified ?? false);
            return null;
        }));

    public TransferState? State(string id)
    {
        lock (Lock) return _transfers.GetValueOrDefault(id);
    }

    public IReadOnlyList<TransferState> All()
    {
        lock (Lock) return _transfers.Values.OrderBy(v => v.LastActivity).ToList();
    }

    /// <summary>The transfers in progress without the slightest event for <paramref name="delay"/>: something got lost on the way.</summary>
    public IReadOnlyList<TransferState> Stuck(DateTimeOffset now, TimeSpan delay)
    {
        lock (Lock)
            return _transfers.Values
                .Where(v => v.Status is TransferStatus.InProgress or TransferStatus.CancellationInProgress && now - v.LastActivity > delay)
                .ToList();
    }
}
