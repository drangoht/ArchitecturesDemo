using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mt.Tenants;

public static class Setup
{
    public static IServiceCollection AddTenants(this IServiceCollection services)
    {
        services.AddSingleton(new TenantCatalog());
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddSingleton<ITenantResolver>(new SubdomainResolver());
        services.AddSingleton<ITenantResolver, HeaderResolver>();
        return services;
    }

    /// <summary>
    /// A limiter PER TENANT (and not a global one): a customer flooding the API exhausts its own quota,
    /// without slowing down its neighbours. It is the answer to the "noisy neighbour" problem.
    /// </summary>
    public static IServiceCollection AddPerTenantRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var tenant = http.RequestServices.GetRequiredService<TenantContext>().Current;
                if (tenant is null)
                    return RateLimitPartition.GetNoLimiter("outside-tenant");

                // The quota is part of the key: after a plan change, a new partition
                // is created with the new quota (otherwise the old one, cached, would stay in force).
                return RateLimitPartition.GetFixedWindowLimiter($"{tenant.Id}:{tenant.Quotas.RequestsPerMinute}", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = tenant.Quotas.RequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.OnRejected = async (context, cancellation) =>
            {
                var http = context.HttpContext;
                var tenant = http.RequestServices.GetRequiredService<TenantContext>().Require();
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = "60";
                await http.Response.WriteAsJsonAsync(new
                {
                    error = $"Quota exceeded for {tenant.Name}: {tenant.Quotas.RequestsPerMinute} requests per minute ({tenant.Plan} plan)."
                }, cancellation);
            };
        });

    /// <summary>To place BEFORE <c>UseRateLimiter</c>: rate limiting needs to know the tenant.</summary>
    public static IApplicationBuilder UseTenants(this IApplicationBuilder app, params string[] pathsOutsideTenant) =>
        app.UseMiddleware<TenantMiddleware>(new PathsOutsideTenant(pathsOutsideTenant));
}
