using System.Net;
using Microsoft.AspNetCore.Http;

namespace Mt.Tenants;

/// <summary>A way to recognize the tenant of a request. Several can coexist.</summary>
public interface ITenantResolver
{
    string Name { get; }
    string? Resolve(HttpContext http);
}

/// <summary><c>alpha.localhost:5114</c> → "alpha". It is the usual choice of SaaS: each customer has its own address.</summary>
public sealed class SubdomainResolver(string rootDomain = "localhost") : ITenantResolver
{
    public string Name => "subdomain";

    public string? Resolve(HttpContext http)
    {
        var host = http.Request.Host.Host;
        if (IPAddress.TryParse(host, out _) || !host.EndsWith("." + rootDomain, StringComparison.OrdinalIgnoreCase))
            return null;
        var subdomain = host[..^(rootDomain.Length + 1)];
        return subdomain.Contains('.') ? null : subdomain;
    }
}

/// <summary>
/// <c>X-Tenant</c> header: handy between services or for an API. On its own, it is declarative: in production,
/// we check that the authenticated user really belongs to this tenant (or read the tenant from their token).
/// </summary>
public sealed class HeaderResolver : ITenantResolver
{
    public const string Header = "X-Tenant";

    public string Name => "header";

    public string? Resolve(HttpContext http) =>
        http.Request.Headers.TryGetValue(Header, out var value) && !string.IsNullOrWhiteSpace(value) ? value.ToString().Trim() : null;
}
