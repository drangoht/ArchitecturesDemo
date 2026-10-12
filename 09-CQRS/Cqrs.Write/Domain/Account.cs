using Cqrs.Events;

namespace Cqrs.Write.Domain;

/// <summary>
/// Aggregate: it protects its invariants and records what happens to it as events.
/// It only exposes what the commands need. No list of operations, no statistics.
/// </summary>
public class Account
{
    private readonly List<IDomainEvent> _events = [];

    public Guid Id { get; private set; }
    public string Owner { get; private set; }
    public decimal Balance { get; private set; }

    public IReadOnlyList<IDomainEvent> Events => _events;

    private Account(Guid id, string owner, decimal balance)
    {
        Id = id;
        Owner = owner;
        Balance = balance;
    }

    public static Account Open(Guid id, string owner, decimal initialDeposit, DateTime date)
    {
        var account = new Account(id, owner, initialDeposit);
        account._events.Add(new AccountOpened(id, owner, initialDeposit, date));
        return account;
    }

    public void Withdraw(decimal amount, DateTime date)
    {
        Debit(amount);
        _events.Add(new MoneyWithdrawn(Id, amount, Balance, date));
    }

    /// <summary>Used by the transfer, which produces its own event (it concerns two accounts).</summary>
    public void Debit(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be strictly positive.");
        if (amount > Balance)
            throw new BusinessRuleException("Insufficient balance.");
        Balance -= amount;
    }

    public void Credit(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be strictly positive.");
        Balance += amount;
    }

    public IReadOnlyList<IDomainEvent> ExtractEvents()
    {
        var events = _events.ToList();
        _events.Clear();
        return events;
    }
}

public class BusinessRuleException(string message) : Exception(message);

public class AccountNotFoundException(Guid accountId) : Exception($"Account {accountId} not found.");
