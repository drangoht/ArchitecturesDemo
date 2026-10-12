using System.Collections.Concurrent;

namespace Mt.Tenants;

/// <summary>
/// The register of the tenants. In production, a table of the platform (often in a separate "catalog" database),
/// with a cache: it is read on every request.
/// </summary>
public sealed class TenantCatalog
{
    private readonly ConcurrentDictionary<string, Tenant> _tenants = new(StringComparer.OrdinalIgnoreCase);

    public TenantCatalog(params IEnumerable<Tenant> initial)
    {
        foreach (var tenant in initial)
            if (!Add(tenant))
                throw new InvalidOperationException($"Duplicate tenant or invalid identifier: \"{tenant.Id}\".");
    }

    public Tenant? Find(string id) => _tenants.GetValueOrDefault(id);

    public IReadOnlyList<Tenant> All() => _tenants.Values.OrderBy(l => l.Id).ToList();

    public bool Add(Tenant tenant) =>
        Tenant.IsValidId(tenant.Id) && _tenants.TryAdd(tenant.Id, tenant);

    public void Replace(Tenant tenant) => _tenants[tenant.Id] = tenant;

    public bool Remove(string id) => _tenants.TryRemove(id, out _);
}
