namespace Res.Bank;

/// <summary>The accounts, in memory. Kept minimal: the subject of the demo is the call to the partner.</summary>
public class Ledger
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Account> _accounts = new()
    {
        [Alice] = new("Alice", 1000m, []),
        [Bob] = new("Bob", 500m, []),
    };

    public AccountView Get(Guid id)
    {
        lock (_gate)
        {
            var account = Find(id);
            return new AccountView(id, account.Owner, account.Balance, [.. account.Operations]);
        }
    }

    public void CheckTransferPossible(Guid source, Guid target, decimal amount)
    {
        lock (_gate)
        {
            var from = Find(source);
            Find(target);
            if (amount > from.Balance)
                throw new BusinessRuleException("Insufficient balance.");
        }
    }

    /// <summary>The balance is checked again here, under the lock: it may have moved during the fraud check.</summary>
    public (decimal Source, decimal Target) Move(Guid source, Guid target, decimal amount, string note)
    {
        lock (_gate)
        {
            var from = Find(source);
            var to = Find(target);
            if (amount > from.Balance)
                throw new BusinessRuleException("Insufficient balance.");
            from.Balance -= amount;
            to.Balance += amount;
            from.Operations.Add($"Transfer to {to.Owner}{note}: -{amount:0.##}");
            to.Operations.Add($"Transfer from {from.Owner}{note}: +{amount:0.##}");
            return (from.Balance, to.Balance);
        }
    }

    private Account Find(Guid id) =>
        _accounts.GetValueOrDefault(id) ?? throw new NotFoundException($"Account {id} not found.");

    private class Account(string owner, decimal balance, List<string> operations)
    {
        public string Owner { get; } = owner;
        public decimal Balance { get; set; } = balance;
        public List<string> Operations { get; } = operations;
    }
}

public record AccountView(Guid Id, string Owner, decimal Balance, IReadOnlyList<string> Operations);

public class BusinessRuleException(string message) : Exception(message);

public class NotFoundException(string message) : Exception(message);
