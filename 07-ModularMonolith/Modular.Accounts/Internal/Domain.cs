using Microsoft.EntityFrameworkCore;
using Modular.Accounts.Contracts;

namespace Modular.Accounts;

internal class Account(Guid id, string owner, decimal balance)
{
    public Guid Id { get; private set; } = id;
    public string Owner { get; private set; } = owner;
    public decimal Balance { get; private set; } = balance;

    public OperationResult Debit(decimal amount)
    {
        if (amount <= 0)
            return OperationResult.Refused("The amount must be strictly positive.");
        if (amount > Balance)
            return OperationResult.Refused("Insufficient balance.");
        Balance -= amount;
        return OperationResult.Ok();
    }

    public OperationResult Credit(decimal amount)
    {
        if (amount <= 0)
            return OperationResult.Refused("The amount must be strictly positive.");
        Balance += amount;
        return OperationResult.Ok();
    }
}

/// <summary>The Accounts module's database. No other module accesses it: they go through IAccountsApi.</summary>
internal class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
}
