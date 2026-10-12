using System.Collections.Concurrent;

namespace Saga.Participants;

/// <summary>Final refusal (insufficient balance, unknown account…): retrying would be pointless.</summary>
public class BusinessRefusalException(string message) : Exception(message);

/// <summary>Transient failure (service unavailable…): retrying later may succeed.</summary>
public class OutageException(string message) : Exception(message);

/// <summary>Demo switches to cause failures at the participants.</summary>
public sealed class Chaos
{
    public int ExchangeFailuresLeft { get; set; }
    public bool ExchangeDown { get; set; }
    public bool NotificationsDown { get; set; }
}

public record AccountView(Guid Id, string Owner, string Currency, decimal Balance, decimal Reserved, decimal Available);

/// <summary>
/// "Accounts" service. A transfer does not debit directly: it first RESERVES the funds (they are no longer
/// available but have not left yet), then CAPTURES them at the end. A reservation is easily released: that is
/// what makes the compensation simple. All the operations are idempotent (key: the saga identifier).
/// </summary>
public sealed class AccountsService
{
    private sealed class Account(Guid id, string owner, string currency, decimal balance)
    {
        public Guid Id { get; } = id;
        public string Owner { get; } = owner;
        public string Currency { get; } = currency;
        public decimal Balance { get; set; } = balance;
        public Dictionary<Guid, decimal> Reservations { get; } = [];
        public Dictionary<Guid, decimal> Credits { get; } = [];
        public decimal Reserved => Reservations.Values.Sum();
        public AccountView View() => new(Id, Owner, Currency, Balance, Reserved, Balance - Reserved);
    }

    private readonly Dictionary<Guid, Account> _accounts = [];
    private readonly Lock _lock = new();

    public void Open(Guid id, string owner, string currency, decimal balance)
    {
        lock (_lock)
            _accounts[id] = new Account(id, owner, currency, balance);
    }

    public AccountView? Get(Guid id)
    {
        lock (_lock)
            return _accounts.GetValueOrDefault(id)?.View();
    }

    public IReadOnlyList<AccountView> All()
    {
        lock (_lock)
            return _accounts.Values.Select(c => c.View()).ToList();
    }

    public string Currency(Guid id) => Find(id).Currency;

    public Task Reserve(Guid saga, Guid accountId, decimal amount) => Modify(accountId, account =>
    {
        if (account.Reservations.ContainsKey(saga))
            return;  // already reserved by this saga: replaying has no effect
        if (amount > account.Balance - account.Reserved)
            throw new BusinessRefusalException($"Insufficient available balance ({account.Balance - account.Reserved:0.00} {account.Currency}).");
        account.Reservations[saga] = amount;
    });

    public Task Release(Guid saga, Guid accountId) => Modify(accountId, account => account.Reservations.Remove(saga));

    /// <summary>Final debit: the reservation becomes a real debit.</summary>
    public Task Capture(Guid saga, Guid accountId) => Modify(accountId, account =>
    {
        if (account.Reservations.Remove(saga, out var amount))
            account.Balance -= amount;
    });

    public Task Credit(Guid saga, Guid accountId, decimal amount) => Modify(accountId, account =>
    {
        if (account.Credits.TryAdd(saga, amount))
            account.Balance += amount;
    });

    public Task CancelCredit(Guid saga, Guid accountId) => Modify(accountId, account =>
    {
        if (account.Credits.Remove(saga, out var amount))
            account.Balance -= amount;
    });

    private Account Find(Guid id)
    {
        lock (_lock)
            return _accounts.GetValueOrDefault(id) ?? throw new BusinessRefusalException($"Account {id} not found.");
    }

    private Task Modify(Guid id, Action<Account> modification)
    {
        lock (_lock)
            modification(Find(id));
        return Task.CompletedTask;
    }
}

public enum ComplianceOpinion { Approved, AwaitingAnalyst, Refused }

/// <summary>"Compliance" service: above a threshold, a human analyst must decide (asynchronous response).</summary>
public sealed class ComplianceService
{
    public const decimal AnalysisThreshold = 10_000m;

    public Task<ComplianceOpinion> Examine(Guid saga, decimal amountInEuros) =>
        Task.FromResult(amountInEuros >= AnalysisThreshold ? ComplianceOpinion.AwaitingAnalyst : ComplianceOpinion.Approved);
}

/// <summary>"Exchange" service. The quote obtained for a saga is kept: asking again gives the same result.</summary>
public sealed class ExchangeService(Chaos chaos)
{
    private static readonly Dictionary<string, decimal> RatesFromEuro = new() { ["EUR"] = 1m, ["USD"] = 1.0832m, ["CHF"] = 0.9415m };
    private readonly ConcurrentDictionary<Guid, decimal> _quotes = new();

    public static decimal InEuros(decimal amount, string currency) => Math.Round(amount / RatesFromEuro[currency], 2);

    public Task<decimal> Convert(Guid saga, decimal amount, string from, string to)
    {
        if (chaos.ExchangeDown)
            throw new OutageException("Exchange service down.");
        if (chaos.ExchangeFailuresLeft > 0)
        {
            chaos.ExchangeFailuresLeft--;
            throw new OutageException("Exchange service temporarily unavailable.");
        }
        return Task.FromResult(_quotes.GetOrAdd(saga, _ => Math.Round(amount / RatesFromEuro[from] * RatesFromEuro[to], 2)));
    }

    public Task CancelQuote(Guid saga)
    {
        _quotes.TryRemove(saga, out _);
        return Task.CompletedTask;
    }
}

/// <summary>"Notifications" service.</summary>
public sealed class NotificationsService(Chaos chaos)
{
    private readonly ConcurrentDictionary<Guid, string> _sent = new();

    public IReadOnlyCollection<string> Sent => _sent.Values.ToList();

    public Task Send(Guid saga, string message)
    {
        if (chaos.NotificationsDown)
            throw new OutageException("Notifications service down.");
        _sent.TryAdd(saga, message);
        return Task.CompletedTask;
    }
}
