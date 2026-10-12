using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Mt.Tenants;

namespace Mt.Bank;

/// <summary>
/// Picks the database of each tenant: the shared database (Standard) or its dedicated database (Premium).
/// It is the only place that knows this difference; the business code and the DbContext know nothing of it.
/// In production, it would be a connection string per tenant, read from the catalog (or from a secrets vault).
/// </summary>
public sealed class BankFactory(InMemoryDatabaseRoot root)
{
    public const string SharedDatabase = "bank-shared";

    private readonly ConcurrentDictionary<string, byte> _databases = new();

    public static string DatabaseName(Tenant tenant) =>
        tenant.Isolation == Isolation.DedicatedDatabase ? $"bank-{tenant.Id}" : SharedDatabase;

    /// <summary>The databases opened so far (for the platform inventory).</summary>
    public IReadOnlyList<string> Databases => _databases.Keys.Order().ToList();

    /// <summary>The database of the current tenant (normal use, in a request).</summary>
    public BankDbContext Open(ITenantContext context) => Open(DatabaseName(context.Require()), context);

    /// <summary>The database of a given tenant, for the platform (registration, migration, deletion).</summary>
    public BankDbContext Open(Tenant tenant) => Open(DatabaseName(tenant), TenantContext.For(tenant));

    /// <summary>
    /// A database seen by the platform, without a tenant: every query fails, except with <c>IgnoreQueryFilters()</c>.
    /// Seeing all the tenants must be an explicit decision, visible in the code.
    /// </summary>
    public BankDbContext OpenWithoutTenant(string databaseName) => Open(databaseName, new TenantContext());

    /// <summary>Deletes a dedicated database (a customer leaving, or after its migration).</summary>
    public async Task DeleteDatabase(Tenant tenant)
    {
        await using var db = Open(tenant);
        await db.Database.EnsureDeletedAsync();
        _databases.TryRemove(DatabaseName(tenant), out _);
    }

    private BankDbContext Open(string databaseName, ITenantContext context)
    {
        _databases.TryAdd(databaseName, 0);
        var options = new DbContextOptionsBuilder<BankDbContext>().UseInMemoryDatabase(databaseName, root).Options;
        return new BankDbContext(options, context);
    }
}
