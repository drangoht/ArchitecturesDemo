using System.Text.RegularExpressions;

namespace Mt.Tenants;

/// <summary>Where a tenant's data is stored.</summary>
public enum Isolation
{
    /// <summary>A database shared by several tenants; each row carries the identifier of its tenant ("pool" model).</summary>
    SharedDatabase,

    /// <summary>A database reserved for the tenant ("silo" model): physical isolation, but a cost per tenant.</summary>
    DedicatedDatabase
}

/// <summary>The subscribed plan: it sets the data isolation and the quotas, not the business rules.</summary>
public enum Plan { Standard, Premium }

/// <summary>
/// The business rules specific to each customer bank: the same code, different parameters.
/// They belong to the contract with the customer, and not to its plan.
/// </summary>
public sealed record TenantRules(decimal TransferLimit, decimal TransferFee, decimal MonthlyFee, int MaxAccounts);

/// <summary>What a tenant is allowed to consume of the shared resources (the "noisy neighbour" is held back).</summary>
public sealed record Quotas(int RequestsPerMinute);

/// <summary>What is declared to register a tenant (configuration file or back office).</summary>
public sealed record TenantDefinition(string Id, string Name, Plan Plan, TenantRules Rules);

/// <summary>A customer of the platform: here, a bank. All the tenants share the same code and the same deployment.</summary>
public sealed record Tenant(string Id, string Name, Plan Plan, Isolation Isolation, TenantRules Rules, Quotas Quotas, bool Active = true)
{
    /// <summary>
    /// The identifier serves as a subdomain (alpha.localhost) and as a dedicated database name: it is restricted to lower case,
    /// digits and hyphens, which also prevents any injection through it.
    /// </summary>
    public static bool IsValidId(string id) => Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{1,29}$");
}

/// <summary>What each plan implies technically.</summary>
public static class Plans
{
    public static Isolation IsolationOf(Plan plan) => plan == Plan.Premium ? Isolation.DedicatedDatabase : Isolation.SharedDatabase;

    public static Quotas QuotasOf(Plan plan) => plan == Plan.Premium ? new Quotas(600) : new Quotas(60);

    public static Tenant Create(TenantDefinition d) =>
        new(d.Id, d.Name, d.Plan, IsolationOf(d.Plan), d.Rules, QuotasOf(d.Plan));

    /// <summary>Same tenant, another plan: the isolation and the quotas follow.</summary>
    public static Tenant Change(Tenant l, Plan plan) =>
        l with { Plan = plan, Isolation = IsolationOf(plan), Quotas = QuotasOf(plan) };
}
