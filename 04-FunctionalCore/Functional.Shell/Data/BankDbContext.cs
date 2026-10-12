using Functional.Core;
using Microsoft.EntityFrameworkCore;

namespace Functional.Shell.Data;

// Mutable persistence model, specific to the shell. The core never sees it.

public class AccountRow
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public decimal Balance { get; set; }

    public Account ToCore() => new(Id, Owner, Balance);
}

public class OperationRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Type { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DateUtc { get; set; }
}

public class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<OperationRow> Operations => Set<OperationRow>();

    public void SeedDemoData()
    {
        if (Accounts.Any())
            return;
        Accounts.AddRange(
            new AccountRow { Id = Alice, Owner = "Alice", Balance = 5000m },
            new AccountRow { Id = Bob, Owner = "Bob", Balance = 500m });
        SaveChanges();
    }
}
