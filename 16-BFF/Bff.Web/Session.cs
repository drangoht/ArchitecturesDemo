using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Bff.Web;

/// <summary>
/// Les sessions, gardées sur le serveur. Le cookie ne contient qu'une clé (chiffrée) vers cette table ; le jeton d'accès,
/// lui, reste ici. Effet de bord utile : se déconnecter supprime la session, et un cookie volé devient inutilisable.
/// En production : un cache distribué (Redis…), pour que toutes les instances du BFF partagent les sessions.
/// </summary>
public sealed class SessionsEnMemoire : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> _sessions = new();

    public int Nombre => _sessions.Count;

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var cle = Guid.NewGuid().ToString("N");
        _sessions[cle] = ticket;
        return Task.FromResult(cle);
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
/// Protection contre la falsification de requêtes (CSRF) : toute requête vers /bff et /api doit porter l'en-tête
/// <c>X-CSRF: 1</c>. Un formulaire d'un autre site ne peut pas ajouter d'en-tête, et un script d'un autre site
/// déclenche une requête préliminaire CORS que ce BFF refuse. Avec <c>SameSite=Strict</c>, c'est une double barrière.
/// </summary>
public sealed class ExigerEnTeteAntiCsrf : IEndpointFilter
{
    public const string EnTete = "X-CSRF";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexte, EndpointFilterDelegate suite) =>
        contexte.HttpContext.Request.Headers[EnTete] == "1"
            ? suite(contexte)
            : ValueTask.FromResult<object?>(Results.Json(new { erreur = $"En-tête {EnTete} manquant." }, statusCode: StatusCodes.Status400BadRequest));
}

public static class InstallationSession
{
    public static IServiceCollection AddSessionBff(this IServiceCollection services)
    {
        services.AddSingleton<SessionsEnMemoire>();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "bff-session";
            o.Cookie.HttpOnly = true;                                 // illisible depuis JavaScript : une faille XSS ne peut pas le voler
            o.Cookie.SameSite = SameSiteMode.Strict;                  // jamais envoyé depuis un autre site
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // en production : Always, et le préfixe « __Host- »
            o.ExpireTimeSpan = TimeSpan.FromMinutes(15);              // comme le jeton d'accès (pas de renouvellement dans la démo)
            o.SlidingExpiration = false;
            // Une API répond 401/403 ; la redirection vers une page de connexion, c'est pour les sites rendus côté serveur.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<SessionsEnMemoire>((o, sessions) => o.SessionStore = sessions);
        services.AddAuthorization();
        return services;
    }
}
