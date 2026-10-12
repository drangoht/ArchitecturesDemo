namespace Ui.Bank;

/// <summary>
/// The bank, in memory, with an adjustable latency to imitate a remote service (0 in the tests and the site,
/// a few hundred milliseconds in the desktop app, to see the interface wait).
/// </summary>
public sealed class InMemoryBank : IBank
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AliceSavings = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly object _lock = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _latency;
    private readonly Dictionary<Guid, Account> _accounts = [];
    private readonly Dictionary<Guid, List<Operation>> _operations = [];

    public InMemoryBank(TimeProvider clock, TimeSpan latency = default)
    {
        _clock = clock;
        _latency = latency;
        var opening = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(2));
        Open(Alice, "Alice", "Current account", 1_000m, opening);
        Open(AliceSavings, "Alice", "Savings account", 8_000m, opening);
        Open(Bob, "Bob", "Current account", 500m, opening);
    }

    public async Task<IReadOnlyList<Account>> AccountsAsync(CancellationToken cancellation = default)
    {
        await Wait(cancellation);
        lock (_lock)
            return _accounts.Values.OrderBy(c => c.Owner).ThenBy(c => c.Label).ToList();
    }

    public async Task<Account> AccountAsync(Guid id, CancellationToken cancellation = default)
    {
        await Wait(cancellation);
        lock (_lock)
            return Find(id);
    }

    public async Task<IReadOnlyList<Operation>> OperationsAsync(Guid account, CancellationToken cancellation = default)
    {
        await Wait(cancellation);
        lock (_lock)
        {
            Find(account);
            return _operations[account].AsEnumerable().Reverse().ToList();
        }
    }

    public async Task<Transfer> TransferAsync(Guid source, Guid target, decimal amount, CancellationToken cancellation = default)
    {
        await Wait(cancellation);
        var error = TransferRules.CheckAmount(amount) ?? TransferRules.CheckAccounts(source, target);
        if (error is not null)
            throw new BusinessError(error);
        lock (_lock)
        {
            var debited = Find(source);
            var credited = Find(target);
            if (TransferRules.CheckBalance(debited.Balance, amount) is { } refusal)
                throw new BusinessError(refusal);
            var now = _clock.GetUtcNow();
            debited = Movement(debited, now, $"Transfer to {credited.Owner} ({credited.Label})", -amount);
            credited = Movement(credited, now, $"Transfer from {debited.Owner} ({debited.Label})", amount);
            return new Transfer(source, target, amount, debited.Balance, credited.Balance);
        }
    }

    private Task Wait(CancellationToken cancellation) =>
        _latency > TimeSpan.Zero ? Task.Delay(_latency, _clock, cancellation) : Task.CompletedTask;

    private Account Find(Guid id) =>
        _accounts.GetValueOrDefault(id) ?? throw new BusinessError($"Unknown account {id}.");

    private void Open(Guid id, string owner, string label, decimal deposit, DateTimeOffset date)
    {
        _accounts[id] = new Account(id, owner, label, 0m);
        _operations[id] = [];
        Movement(_accounts[id], date, "Opening deposit", deposit);
    }

    private Account Movement(Account account, DateTimeOffset date, string label, decimal amount)
    {
        _operations[account.Id].Add(new Operation(date, label, amount));
        return _accounts[account.Id] = account with { Balance = account.Balance + amount };
    }
}
