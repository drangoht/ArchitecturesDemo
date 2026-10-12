using Microsoft.EntityFrameworkCore;
using Vertical.Api.Domain;

namespace Vertical.Api.Data;

/// <summary>
/// No repository on top: the DbContext is already an abstraction (Unit of Work + Repository).
/// Each slice uses it directly, in its own way.
/// </summary>
public class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Operation> Operations => Set<Operation>();

    public void SeedDemoData()
    {
        if (Accounts.Any())
            return;
        Accounts.AddRange(new Account(Alice, "Alice", 1000m), new Account(Bob, "Bob", 500m));
        SaveChanges();
    }
}
