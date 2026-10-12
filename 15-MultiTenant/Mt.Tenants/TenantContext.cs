namespace Mt.Tenants;

/// <summary>
/// "For which tenant am I working?" One instance per request (or per background task).
/// The business code and the data access only read the tenant here, never in the HTTP request.
/// </summary>
public interface ITenantContext
{
    Tenant? Current { get; }

    /// <summary>The current tenant, or an exception: without a tenant, we refuse to work rather than see everything.</summary>
    Tenant Require();
}

public sealed class NoTenantException()
    : InvalidOperationException("No tenant set: data access refused.");

public sealed class TenantContext : ITenantContext
{
    public Tenant? Current { get; private set; }

    public static TenantContext For(Tenant tenant)
    {
        var context = new TenantContext();
        context.Set(tenant);
        return context;
    }

    /// <summary>Set once, at the start of the request: changing tenant along the way is refused.</summary>
    public void Set(Tenant tenant)
    {
        if (Current is not null && !string.Equals(Current.Id, tenant.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The context already belongs to tenant \"{Current.Id}\".");
        Current = tenant;
    }

    public Tenant Require() => Current ?? throw new NoTenantException();
}
