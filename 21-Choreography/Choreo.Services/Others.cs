using Choreo.Bus;
using Choreo.Services.Contracts;

namespace Choreo.Services
{
    /// <summary>The exchange rates (from the euro), known to the Exchange service and, for amounts in euros, to Accounts.</summary>
    public static class Rates
    {
        public static readonly IReadOnlyDictionary<string, decimal> FromEuro =
            new Dictionary<string, decimal> { ["EUR"] = 1m, ["USD"] = 1.0832m, ["CHF"] = 0.9415m };

        /// <summary>A currency not quoted is counted at par: it is up to the Exchange service to refuse it.</summary>
        public static decimal InEuros(decimal amount, string currency) =>
            FromEuro.TryGetValue(currency, out var rate) ? Math.Round(amount / rate, 2) : amount;
    }

    /// <summary>
    /// The pattern common to all the services: react only once to an event (the "inbox": an event
    /// delivered twice acts only once), and change the state AND publish the next one in one go (otherwise: the outbox of demo 8).
    /// </summary>
    public abstract class Reactive(Choreo.Bus.Bus bus, string name)
    {
        private readonly HashSet<Guid> _handled = new();
        protected readonly Lock Lock = new();
        protected Choreo.Bus.Bus Bus { get; } = bus;
        public string Name { get; } = name;

        protected void Once(Event received, Func<Event?> react)
        {
            lock (Lock)
            {
                if (_handled.Contains(received.Id))
                    return;
                if (react() is { } next)
                    Bus.Publish(next);
                _handled.Add(received.Id);
            }
        }
    }
}

namespace Choreo.Services.Compliance
{
    /// <summary>
    /// Compliance: blocklist, and beyond 10,000 € a human analyst decides (later, through the API).
    /// It also listens to the expiry of the reservations: no use having an analyst decide on an abandoned transfer.
    /// </summary>
    public sealed class ComplianceService(Choreo.Bus.Bus bus) : Reactive(bus, "Compliance")
    {
        public const decimal AnalysisThreshold = 10_000m;
        public static readonly Guid Mallory = Guid.Parse("66666666-6666-6666-6666-666666666666");

        private readonly Dictionary<string, CompliancePending> _pending = new();

        public void Register()
        {
            Bus.Subscribe<FundsReserved>(Name, Examine);
            Bus.Subscribe<ReservationExpired>(Name, e => Once(e, () =>
            {
                _pending.Remove(e.Correlation);
                return null;
            }));
        }

        private void Examine(FundsReserved e) => Once(e, () =>
        {
            if (e.Order.Target == Mallory)
                return e.Next(new ComplianceRefused(e.Order, "payee on blocklist"), Name);
            if (e.Order.AmountEur < AnalysisThreshold)
                return e.Next(new ComplianceApproved(e.Order), Name);
            var pending = e.Next(new CompliancePending(e.Order), Name);
            _pending[e.Correlation] = pending;
            return pending;
        });

        public IReadOnlyList<string> Pending()
        {
            lock (Lock) return _pending.Keys.Order().ToList();
        }

        /// <summary>An analyst's decision: an event coming from a human, in response to CompliancePending.</summary>
        public bool Decide(string transfer, bool approved)
        {
            lock (Lock)
            {
                if (!_pending.Remove(transfer, out var pending))
                    return false;
                Bus.Publish<TransferEvent>(approved
                    ? pending.Next(new ComplianceApproved(pending.Order), Name)
                    : pending.Next(new ComplianceRefused(pending.Order, "refused by an analyst"), Name));
                return true;
            }
        }
    }
}

namespace Choreo.Services.Exchange
{
    /// <summary>The exchange: it converts the amount into the payee's currency. Can be broken down (exception).</summary>
    public sealed class ExchangeService(Choreo.Bus.Bus bus) : Reactive(bus, "Exchange")
    {
        public volatile bool Down;

        public void Register() => Bus.Subscribe<ComplianceApproved>(Name, Convert);

        private void Convert(ComplianceApproved e)
        {
            if (Down)
                throw new InvalidOperationException("Exchange service unavailable.");
            Once(e, () =>
            {
                var o = e.Order;
                if (!Rates.FromEuro.TryGetValue(o.SourceCurrency, out var fromRate) || !Rates.FromEuro.TryGetValue(o.TargetCurrency, out var toRate))
                    return e.Next(new ExchangeFailed(o, $"currency not quoted ({o.SourceCurrency} → {o.TargetCurrency})"), Name);
                return e.Next(new CurrenciesConverted(o with { TargetAmount = Math.Round(o.Amount / fromRate * toRate, 2) }), Name);
            });
        }
    }
}

namespace Choreo.Services.Notifications
{
    /// <summary>Tells the customer once the transfer is settled. After the point of no return: on failure, we retry, we undo nothing.</summary>
    public sealed class NotificationsService(Choreo.Bus.Bus bus) : Reactive(bus, "Notifications")
    {
        private readonly List<string> _sent = [];
        private int _remainingFailures;

        public void Register() => Bus.Subscribe<TransferSettled>(Name, Notify);

        /// <summary>The next N attempts will fail (email server unavailable).</summary>
        public void Sabotage(int failures) => Interlocked.Exchange(ref _remainingFailures, failures);

        private void Notify(TransferSettled e)
        {
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
                throw new InvalidOperationException("Email server unavailable.");
            Once(e, () =>
            {
                var message = $"Transfer of {e.Order.Amount:0.00} {e.Order.SourceCurrency} done ({e.Order.TargetAmount:0.00} {e.Order.TargetCurrency} received).";
                _sent.Add(message);
                return e.Next(new CustomerNotified(e.Order, message), Name);
            });
        }

        public IReadOnlyList<string> Sent()
        {
            lock (Lock) return _sent.ToList();
        }
    }
}

namespace Choreo.Services.Loyalty
{
    /// <summary>
    /// The service added AFTERWARDS: loyalty points on each settled transfer. Subscribing is enough;
    /// no other service was modified, none knows it exists.
    /// </summary>
    public sealed class LoyaltyService(Choreo.Bus.Bus bus) : Reactive(bus, "Loyalty")
    {
        private readonly Dictionary<Guid, int> _points = new();

        public void Register() => Bus.Subscribe<TransferSettled>(Name, e => Once(e, () =>
        {
            _points[e.Order.Source] = _points.GetValueOrDefault(e.Order.Source) + (int)(e.Order.AmountEur / 10);
            return null;
        }));

        public int Points(Guid account)
        {
            lock (Lock) return _points.GetValueOrDefault(account);
        }
    }
}
