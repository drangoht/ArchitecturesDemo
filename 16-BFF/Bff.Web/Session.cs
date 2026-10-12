using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Bff.Web;

/// <summary>
/// The sessions, kept on the server. The cookie only contains an (encrypted) key to this table; the access token
/// itself stays here. Useful side effect: logging out deletes the session, and a stolen cookie becomes unusable.
/// In production: a distributed cache (Redis…), so that all the instances of the BFF share the sessions.
/// </summary>
public sealed class InMemorySessions : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> _sessions = new();

    public int Count => _sessions.Count;

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        _sessions[key] = ticket;
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _sessions[key] = ticket;
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult(_sessions.GetValueOrDefault(key));

    public Task RemoveAsync(string key)
    {
        _sessions.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Protection against cross-site request forgery (CSRF): every request to /bff and /api must carry the header
/// <c>X-CSRF: 1</c>. A form on another site cannot add a header, and a script on another site
/// triggers a CORS preflight request that this BFF refuses. With <c>SameSite=Strict</c>, it is a double barrier.
/// </summary>
public sealed class RequireAntiCsrfHeader : IEndpointFilter
{
    public const string Header = "X-CSRF";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers[Header] == "1"
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Json(new { error = $"Missing {Header} header." }, statusCode: StatusCodes.Status400BadRequest));
}

public static class SessionSetup
{
    public static IServiceCollection AddBffSession(this IServiceCollection services)
    {
        services.AddSingleton<InMemorySessions>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "bff-session";
            o.Cookie.HttpOnly = true;                                 // unreadable from JavaScript: an XSS flaw cannot steal it
            o.Cookie.SameSite = SameSiteMode.Strict;                  // never sent from another site
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // in production: Always, and the "__Host-" prefix
            o.ExpireTimeSpan = TimeSpan.FromMinutes(15);              // like the access token (no renewal in the demo)
            o.SlidingExpiration = false;
            // An API answers 401/403; redirecting to a login page is for server-rendered sites.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<InMemorySessions>((o, sessions) => o.SessionStore = sessions);
        services.AddAuthorization();
        return services;
    }
}
