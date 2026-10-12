using Microsoft.AspNetCore.Http;

namespace Mt.Tenants;

/// <summary>
/// The entry point of tenancy: identifies the tenant BEFORE everything else (rate limiting, business code, data)
/// and stores it in the request's <see cref="TenantContext"/>. A request without a valid tenant never reaches the business code.
/// </summary>
public sealed class TenantMiddleware(
    RequestDelegate next, IEnumerable<ITenantResolver> resolvers, TenantCatalog catalog, PathsOutsideTenant outsideTenant)
{
    public async Task InvokeAsync(HttpContext http, TenantContext context)
    {
        if (outsideTenant.Paths.Any(c => http.Request.Path.StartsWithSegments(c)))
        {
            await next(http);
            return;
        }

        var found = resolvers
            .Select(r => (r.Name, Id: r.Resolve(http)))
            .Where(r => r.Id is not null)
            .ToList();
        var identifiers = found.Select(r => r.Id!.ToLowerInvariant()).Distinct().ToList();

        if (identifiers.Count == 0)
        {
            await Refuse(http, StatusCodes.Status400BadRequest,
                $"Tenant not specified: use a subdomain (alpha.localhost) or the {HeaderResolver.Header} header.");
            return;
        }
        if (identifiers.Count > 1)
        {
            // Subdomain "alpha" and header "beta": we don't pick one, we refuse.
            await Refuse(http, StatusCodes.Status400BadRequest,
                "Ambiguous tenant: " + string.Join(", ", found.Select(r => $"{r.Name} = {r.Id}")));
            return;
        }

        var tenant = catalog.Find(identifiers[0]);
        if (tenant is null)
        {
            await Refuse(http, StatusCodes.Status404NotFound, $"Unknown tenant: \"{identifiers[0]}\".");
            return;
        }
        if (!tenant.Active)
        {
            await Refuse(http, StatusCodes.Status403Forbidden, $"Tenant \"{tenant.Name}\" is suspended.");
            return;
        }

        context.Set(tenant);
        http.Response.Headers[HeaderResolver.Header] = tenant.Id;
        await next(http);
    }

    private static Task Refuse(HttpContext http, int status, string error)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { error });
    }
}

/// <summary>The routes that run without a tenant (the platform back office).</summary>
public sealed record PathsOutsideTenant(IReadOnlyList<string> Paths);
