namespace Mt.Bank;

/// <summary>
/// Marks a piece of data that belongs to a tenant. <see cref="BankDbContext"/> uses it to filter
/// all the queries and stamp all the insertions: implementing the interface is enough, impossible to forget.
/// </summary>
public interface IBelongsToTenant
{
    string TenantId { get; }
}

public sealed class Account : IBelongsToTenant
{
    private Account() { }

    public Account(Guid id, string owner, decimal balance)
    {
        Id = id;
        Owner = owner;
        Balance = balance;
    }

    /// <summary>Never set by the business code: filled in by the DbContext when saving.</summary>
    public string TenantId { get; private set; } = "";
    public Guid Id { get; private set; }
    public string Owner { get; private set; } = "";
    public decimal Balance { get; private set; }

    public void Debit(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be positive.");
        if (amount > Balance)
            throw new BusinessRuleException($"Insufficient balance ({Balance:0.00} €).");
        Balance -= amount;
    }

    public void Credit(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be positive.");
        Balance += amount;
    }
}

public sealed class Operation : IBelongsToTenant
{
    private Operation() { }

    public Operation(Guid accountId, DateTimeOffset date, string label, decimal amount, string? key = null)
    {
        Id = Guid.NewGuid();
        AccountId = accountId;
        Date = date;
        Label = label;
        Amount = amount;
        Key = key;
    }

    public string TenantId { get; private set; } = "";
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public DateTimeOffset Date { get; private set; }
    public string Label { get; private set; } = "";
    public decimal Amount { get; private set; }

    /// <summary>Idempotency key (e.g. "fee-2026-10"): a replayed charge does not debit twice.</summary>
    public string? Key { get; private set; }
}

public sealed class BusinessRuleException(string message) : Exception(message);

public sealed class AccountNotFoundException(Guid id) : Exception($"Account {id} not found.");

/// <summary>A write targeted data of another tenant: it is a bug, never a business case.</summary>
public sealed class CrossTenantLeakException(string message) : InvalidOperationException(message);

public sealed record AccountDto(Guid Id, string Owner, decimal Balance);

public sealed record OperationDto(DateTimeOffset Date, string Label, decimal Amount);

public sealed record TransferDto(Guid Source, Guid Target, decimal Amount, decimal Fee, decimal SourceBalance);
