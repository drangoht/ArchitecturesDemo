using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Mt.Tenants;

namespace Mt.Bank;

/// <summary>
/// The isolation safety net. Three protections, so that the business code never has to think about the tenant:
/// ① a global filter adds "WHERE TenantId = @current" to ALL the queries;
/// ② each insertion is stamped with the current tenant;
/// ③ modifying or deleting a row of another tenant throws an exception.
/// Without a tenant, every query fails (<see cref="NoTenantException"/>): closed by default.
/// </summary>
public sealed class BankDbContext(DbContextOptions<BankDbContext> options, ITenantContext context) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Operation> Operations => Set<Operation>();

    /// <summary>
    /// Member of the DbContext: EF reads it again for EACH query. The model (and its filter) is built only once
    /// for the whole application, but each DbContext instance filters on its own tenant.
    /// </summary>
    private string CurrentTenant => context.Require().Id;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>();
        model.Entity<Operation>().HasIndex(o => o.Key);

        // ① Applied to all the entities that implement IBelongsToTenant, discovered by reflection:
        //   a new entity is protected without having to think about it (and a test checks it).
        var method = typeof(BankDbContext).GetMethod(nameof(Isolate), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in model.Model.GetEntityTypes().Select(t => t.ClrType).Where(typeof(IBelongsToTenant).IsAssignableFrom).ToList())
            method.MakeGenericMethod(type).Invoke(this, [model]);
    }

    private void Isolate<T>(ModelBuilder model) where T : class, IBelongsToTenant
    {
        // Composite key: two tenants can have an account with the same identifier (here, Alice and Bob exist everywhere)
        // without conflict in the shared database.
        model.Entity<T>().HasKey(nameof(IBelongsToTenant.TenantId), "Id");
        model.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenant);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellation = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellation);
    }

    private void Stamp()
    {
        foreach (var entry in ChangeTracker.Entries<IBelongsToTenant>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == "")
            {
                // ② The insertion takes the current tenant.
                entry.Property(nameof(IBelongsToTenant.TenantId)).CurrentValue = CurrentTenant;
            }
            else if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                     && entry.Entity.TenantId != CurrentTenant)
            {
                // ③ An entity of another tenant has nothing to do here.
                throw new CrossTenantLeakException(
                    $"{entry.Entity.GetType().Name} of tenant \"{entry.Entity.TenantId}\" modified on behalf of \"{CurrentTenant}\".");
            }
        }
    }
}
