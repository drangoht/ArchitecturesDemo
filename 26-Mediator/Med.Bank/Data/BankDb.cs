using Med.Bank.Domain;
using Microsoft.EntityFrameworkCore;

namespace Med.Bank.Data;

/// <summary>
/// One model for everything, writes and reads: going through a mediator does not make it CQRS.
/// The handlers never call SaveChanges: the UnitOfWorkBehavior does it, once per command.
/// </summary>
public class BankDb(DbContextOptions<BankDb> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ComplianceAlert> ComplianceAlerts => Set<ComplianceAlert>();

    public void SeedDemoData()
    {
        if (Accounts.Any())
            return;
        Accounts.AddRange(new Account(Alice, "Alice", 1000m), new Account(Bob, "Bob", 500m));
        SaveChanges();
    }
}
