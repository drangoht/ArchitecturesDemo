using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mt.Locataires;

public static class Installation
{
    public static IServiceCollection AddLocataires(this IServiceCollection services)
    {
        services.AddSingleton(new CatalogueLocataires());
        services.AddScoped<ContexteLocataire>();
        services.AddScoped<IContexteLocataire>(sp => sp.GetRequiredService<ContexteLocataire>());
        services.AddSingleton<IResolveurLocataire>(new ResolveurParSousDomaine());
        services.AddSingleton<IResolveurLocataire, ResolveurParEnTete>();
        return services;
    }

    /// <summary>
    /// Un limiteur PAR LOCATAIRE (et non global) : un client qui inonde l'API épuise son propre quota,
    /// sans ralentir ses voisins. C'est la réponse au problème du « voisin bruyant ».
    /// </summary>
    public static IServiceCollection AddLimitationParLocataire(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var locataire = http.RequestServices.GetRequiredService<ContexteLocataire>().Courant;
                if (locataire is null)
                    return RateLimitPartition.GetNoLimiter("hors-locataire");

                // Le quota fait partie de la clé : après un changement de formule, une nouvelle partition
                // est créée avec le nouveau quota (sinon l'ancienne, mise en cache, resterait en vigueur).
                return RateLimitPartition.GetFixedWindowLimiter($"{locataire.Id}:{locataire.Quotas.RequetesParMinute}", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = locataire.Quotas.RequetesParMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.OnRejected = async (contexte, annulation) =>
            {
                var http = contexte.HttpContext;
                var locataire = http.RequestServices.GetRequiredService<ContexteLocataire>().Exiger();
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = "60";
                await http.Response.WriteAsJsonAsync(new
                {
                    erreur = $"Quota dépassé pour {locataire.Nom} : {locataire.Quotas.RequetesParMinute} requêtes par minute (formule {locataire.Plan})."
                }, annulation);
            };
        });

    /// <summary>À placer AVANT <c>UseRateLimiter</c> : la limitation a besoin de connaître le locataire.</summary>
    public static IApplicationBuilder UseLocataires(this IApplicationBuilder app, params string[] cheminsHorsLocataire) =>
        app.UseMiddleware<MiddlewareLocataire>(new CheminsHorsLocataire(cheminsHorsLocataire));
}
