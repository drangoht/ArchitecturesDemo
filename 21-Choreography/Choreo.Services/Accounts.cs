using Choreo.Bus;
using Choreo.Services.Contracts;

namespace Choreo.Services.Accounts;

public sealed record AccountDto(Guid Id, string Owner, string Currency, decimal Balance, decimal Reserved);

/// <summary>
/// The Accounts service: it reserves, settles, releases. It must KNOW which events of the others mean "undo what
/// you did" (a compliance refusal, an exchange failure): the compensation is scattered among the services.
/// And since nobody drives the process, it is the one that protects itself from a process left hanging: its reservation expires.
/// </summary>
public sealed class AccountsService(Choreo.Bus.Bus bus, TimeProvider clock, TimeSpan reservationDuration) : Reactive(bus, "Accounts")
{
    private sealed record Reservation(Order Order, DateTimeOffset ExpiresAt);

    private readonly Dictionary<Guid, (string Owner, string Currency, decimal Balance, decimal Reserved)> _accounts = new();
    private readonly Dictionary<string, Reservation> _reservations = new();
    private readonly HashSet<string> _settled = new();

    public void Register()
    {
        Bus.Subscribe<TransferRequested>(Name, Reserve);
        Bus.Subscribe<ComplianceRefused>(Name, e => Release(e, e.Reason));  // compensation
        Bus.Subscribe<ExchangeFailed>(Name, e => Release(e, e.Reason));       // compensation
        Bus.Subscribe<CurrenciesConverted>(Name, Settle);
    }

    public void Open(Guid id, string owner, string currency, decimal balance)
    {
        lock (Lock) _accounts[id] = (owner, currency, balance, 0m);
    }

    public AccountDto? Get(Guid id)
    {
        lock (Lock)
            return _accounts.TryGetValue(id, out var c) ? new AccountDto(id, c.Owner, c.Currency, c.Balance, c.Reserved) : null;
    }

    private void Reserve(TransferRequested e) => Once(e, () =>
    {
        var o = e.Order;
        if (!_accounts.TryGetValue(o.Source, out var source) || !_accounts.TryGetValue(o.Target, out var target))
            return e.Next(new FundsRefused(o, "unknown account"), Name);
        if (o.Amount <= 0 || o.Source == o.Target)
            return e.Next(new FundsRefused(o, "invalid transfer"), Name);
        if (source.Balance - source.Reserved < o.Amount)
            return e.Next(new FundsRefused(o, $"insufficient available balance ({source.Balance - source.Reserved:0.00} {source.Currency})"), Name);

        _accounts[o.Source] = source with { Reserved = source.Reserved + o.Amount };
        // The service enriches the order with what it alone knows: the currencies of both accounts.
        var enriched = o with { SourceCurrency = source.Currency, TargetCurrency = target.Currency, AmountEur = Rates.InEuros(o.Amount, source.Currency) };
        _reservations[e.Correlation] = new Reservation(enriched, clock.GetUtcNow() + reservationDuration);
        return e.Next(new FundsReserved(enriched), Name);
    });

    private void Release(TransferEvent e, string reason) => Once(e, () =>
    {
        if (!_reservations.Remove(e.Correlation, out var reservation))
            return null;  // already released (expired): nothing to undo
        GiveBack(reservation.Order);
        return e.Next(new FundsReleased(reservation.Order, reason), Name);
    });

    private void Settle(CurrenciesConverted e) => Once(e, () =>
    {
        if (!_reservations.Remove(e.Correlation, out var reservation))
            return _settled.Contains(e.Correlation) ? null
                : e.Next(new TransferAbandoned(e.Order, "the reservation expired before the settlement"), Name);

        var o = e.Order;
        var source = _accounts[o.Source];
        var target = _accounts[o.Target];
        _accounts[o.Source] = source with { Balance = source.Balance - reservation.Order.Amount, Reserved = source.Reserved - reservation.Order.Amount };
        _accounts[o.Target] = target with { Balance = target.Balance + o.TargetAmount };
        _settled.Add(e.Correlation);
        return e.Next(new TransferSettled(o), Name);
    });

    /// <summary>
    /// Called regularly: the reservations that are too old are released. It is the safety net of the choreography:
    /// without it, a failed service or an analyst who never answers would block Alice's money forever.
    /// </summary>
    public int ExpireReservations()
    {
        List<ReservationExpired> expired;
        lock (Lock)
        {
            var now = clock.GetUtcNow();
            expired = _reservations.Where(r => r.Value.ExpiresAt <= now).ToList()
                .Select(r =>
                {
                    _reservations.Remove(r.Key);
                    GiveBack(r.Value.Order);
                    return new ReservationExpired(r.Value.Order) { Correlation = r.Key, Emitter = Name };
                }).ToList();
            foreach (var e in expired)
                Bus.Publish(e);
        }
        return expired.Count;
    }

    private void GiveBack(Order o)
    {
        var source = _accounts[o.Source];
        _accounts[o.Source] = source with { Reserved = source.Reserved - o.Amount };
    }
}
