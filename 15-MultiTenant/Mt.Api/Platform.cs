using Microsoft.EntityFrameworkCore;
using Mt.Bank;
using Mt.Tenants;

namespace Mt.Api;

public sealed record MoveReport(string Tenant, string From, string To, int Accounts, int Operations);

public sealed record FeeReport(string Tenant, decimal MonthlyFee, int AccountsCharged, string? Note = null);

public sealed record RowsPerTenant(string Tenant, int Accounts, int Operations);

public sealed record DatabaseInventory(string Database, IReadOnlyList<RowsPerTenant> Tenants);

/// <summary>
/// The vendor's back office: it works ON the tenants, not FOR one of them. It is the only code that handles
/// several tenants, and it always does so explicitly (designated tenant, or IgnoreQueryFilters).
/// In production, these operations are reserved for the platform administrators.
/// </summary>
public sealed class PlatformService(TenantCatalog catalog, BankFactory factory, IServiceScopeFactory scopes, TimeProvider clock)
{
    /// <summary>A customer arrives: provision its data, then open it. A few seconds, no deployment.</summary>
    public async Task<Tenant> Register(TenantDefinition definition, IEnumerable<Account>? initialAccounts = null)
    {
        if (!Tenant.IsValidId(definition.Id))
            throw new BusinessRuleException("Invalid identifier: 2 to 30 characters, lower case, digits and hyphens.");
        if (catalog.Find(definition.Id) is not null)
            throw new BusinessRuleException($"Tenant \"{definition.Id}\" already exists.");

        var tenant = Plans.Create(definition);
        await using (var db = factory.Open(tenant))
        {
            // Premium: creates its dedicated database. Standard: the shared database already exists, nothing to create.
            await db.Database.EnsureCreatedAsync();
            foreach (var account in initialAccounts ?? [])
            {
                db.Accounts.Add(account);
                db.Operations.Add(new Operation(account.Id, clock.GetUtcNow(), "Initial deposit", account.Balance));
            }
            await db.SaveChangesAsync();
        }
        catalog.Add(tenant);
        return tenant;
    }

    public bool Suspend(string id, bool suspended)
    {
        if (catalog.Find(id) is not { } tenant)
            return false;
        catalog.Replace(tenant with { Active = !suspended });
        return true;
    }

    /// <summary>
    /// A customer leaves: erase ALL its data (GDPR, end of contract).
    /// Dedicated database: the database is deleted. Shared database: its rows are deleted, one table after the other.
    /// </summary>
    public async Task<MoveReport?> Delete(string id)
    {
        if (catalog.Find(id) is not { } tenant)
            return null;
        // Cut access first: no more request arrives during the erasure.
        catalog.Replace(tenant with { Active = false });

        var report = await Count(tenant, "(deleted)");
        if (tenant.Isolation == Isolation.DedicatedDatabase)
            await factory.DeleteDatabase(tenant);
        else
            await EmptySharedDatabase(tenant);

        catalog.Remove(id);
        return report;
    }

    /// <summary>
    /// Plan change. If the isolation changes (Standard ↔ Premium), the data moves from one database to the other;
    /// the tenant is suspended during the move, so that no write gets lost between the two databases.
    /// </summary>
    public async Task<MoveReport?> ChangePlan(string id, Plan plan)
    {
        if (catalog.Find(id) is not { } before)
            return null;
        var after = Plans.Change(before, plan);
        if (before.Isolation == after.Isolation)
        {
            catalog.Replace(after);
            return new MoveReport(id, BankFactory.DatabaseName(before), BankFactory.DatabaseName(after), 0, 0);
        }

        catalog.Replace(before with { Active = false });
        try
        {
            MoveReport report;
            await using (var source = factory.Open(before))
            await using (var destination = factory.Open(after))
            {
                // The global filter does the sorting: the source only returns the rows of THIS tenant.
                var accounts = await source.Accounts.AsNoTracking().ToListAsync();
                var operations = await source.Operations.AsNoTracking().ToListAsync();
                await destination.Database.EnsureCreatedAsync();
                destination.Accounts.AddRange(accounts);
                destination.Operations.AddRange(operations);
                await destination.SaveChangesAsync();
                report = new MoveReport(id, BankFactory.DatabaseName(before), BankFactory.DatabaseName(after), accounts.Count, operations.Count);
            }

            // The old location is erased only once the copy is saved.
            if (before.Isolation == Isolation.DedicatedDatabase)
                await factory.DeleteDatabase(before);
            else
                await EmptySharedDatabase(before);

            catalog.Replace(after with { Active = before.Active });
            return report;
        }
        catch
        {
            catalog.Replace(before);
            throw;
        }
    }

    /// <summary>What each database contains, tenant by tenant: the two isolation models are seen side by side.</summary>
    public async Task<IReadOnlyList<DatabaseInventory>> Inventory()
    {
        var inventory = new List<DatabaseInventory>();
        foreach (var name in factory.Databases)
        {
            await using var db = factory.OpenWithoutTenant(name);
            // Without IgnoreQueryFilters(), these queries would throw NoTenantException.
            var accounts = await db.Accounts.IgnoreQueryFilters().Select(c => c.TenantId).ToListAsync();
            var operations = await db.Operations.IgnoreQueryFilters().Select(o => o.TenantId).ToListAsync();
            var rows = accounts.Concat(operations).Distinct().Order()
                .Select(l => new RowsPerTenant(l, accounts.Count(c => c == l), operations.Count(o => o == l)))
                .ToList();
            inventory.Add(new DatabaseInventory(name, rows));
        }
        return inventory;
    }

    /// <summary>
    /// A background task has no HTTP request, hence no middleware to set the tenant: it must do it itself,
    /// tenant by tenant, each in its own scope (its DbContext, its rules). If it forgot to, the DbContext
    /// would throw NoTenantException instead of working on all the banks at once.
    /// </summary>
    public async Task<IReadOnlyList<FeeReport>> ChargeMonthlyFees(string month)
    {
        var reports = new List<FeeReport>();
        foreach (var tenant in catalog.All())
        {
            if (!tenant.Active)
            {
                reports.Add(new FeeReport(tenant.Id, tenant.Rules.MonthlyFee, 0, "suspended: skipped"));
                continue;
            }
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
            var charged = await scope.ServiceProvider.GetRequiredService<BankingService>().ChargeMonthlyFees(month);
            reports.Add(new FeeReport(tenant.Id, tenant.Rules.MonthlyFee, charged));
        }
        return reports;
    }

    private async Task<MoveReport> Count(Tenant tenant, string to)
    {
        await using var db = factory.Open(tenant);
        return new MoveReport(tenant.Id, BankFactory.DatabaseName(tenant), to,
            await db.Accounts.CountAsync(), await db.Operations.CountAsync());
    }

    private async Task EmptySharedDatabase(Tenant tenant)
    {
        await using var db = factory.Open(tenant);
        db.Operations.RemoveRange(await db.Operations.ToListAsync());
        db.Accounts.RemoveRange(await db.Accounts.ToListAsync());
        await db.SaveChangesAsync();
    }
}
