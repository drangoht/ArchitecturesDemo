using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sls.Plateforme;

public record ValeurReglage(string Valeur);

public static class PlateformeExtensions
{
    /// <summary>« Déploie » les fonctions décrites par le manifeste sur la plateforme.</summary>
    public static IServiceCollection AddPlateformeServerless(this IServiceCollection services, Action<Manifeste> deploiement)
    {
        var manifeste = new Manifeste();
        deploiement(manifeste);

        services.AddSingleton(manifeste);
        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection("Plateforme").Get<OptionsPlateforme>() ?? new OptionsPlateforme());
        services.AddSingleton(sp => new ServicesGeres(
            sp.GetRequiredService<OptionsPlateforme>(),
            sp.GetRequiredService<IConfiguration>().GetSection("Reglages").AsEnumerable(makePathsRelative: true),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Plateforme")));
        services.AddSingleton<MoteurExecution>();
        services.AddHostedService<DistributeurFiles>();
        services.AddHostedService<Minuteries>();
        services.AddHostedService<Recycleur>();
        return services;
    }

    public static void MapPlateformeServerless(this WebApplication app)
    {
        var moteur = app.Services.GetRequiredService<MoteurExecution>();
        var services = app.Services.GetRequiredService<ServicesGeres>();

        // Passerelle HTTP : chaque route du manifeste déclenche sa fonction.
        foreach (var definition in moteur.Definitions.Where(d => d.Type == TypeDeclencheur.Http))
            app.MapMethods(definition.Route!, [definition.Methode!],
                (Func<HttpContext, Task<IResult>>)(http => InvoquerHttp(http, definition.Nom, moteur)));

        // « Console » du fournisseur cloud : observer, configurer, réparer.
        var console = app.MapGroup("/plateforme");
        console.MapGet("/fonctions", () => moteur.Statistiques());
        console.MapGet("/facture", () => moteur.Facture());
        console.MapGet("/journal", (int? nombre) => services.Journal.Dernieres(nombre ?? 50));
        console.MapGet("/files", () => services.Files.Etat());
        console.MapPost("/files/{file}/rejouer", (string file) => Results.Ok(new { rejoues = services.Files.Rejouer(file) }));
        console.MapGet("/reglages", () => services.Reglages);
        console.MapPut("/reglages/{cle}", (string cle, ValeurReglage reglage) =>
        {
            services.Reglages[cle] = reglage.Valeur;
            return Results.NoContent();
        });
        console.MapGet("/stockage", (string? prefixe) => services.Stockage.Lister(prefixe ?? ""));
        console.MapGet("/stockage/contenu", (string chemin) =>
            services.Stockage.Lire(chemin) is { } contenu ? Results.Text(contenu) : Results.NotFound());
        console.MapPost("/minuteries/{fonction}/declencher", async (string fonction) =>
        {
            await Minuteries.Declencher(moteur, services, fonction);
            return Results.Accepted();
        });
    }

    private static async Task<IResult> InvoquerHttp(HttpContext http, string nom, MoteurExecution moteur)
    {
        using var lecteur = new StreamReader(http.Request.Body);
        var requete = new RequeteHttp(http.Request.Method, http.Request.Path,
            http.Request.RouteValues.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? ""),
            await lecteur.ReadToEndAsync());
        try
        {
            var resultat = await moteur.Invoquer(nom, requete);
            var reponse = (ReponseHttp)resultat.Valeur!;

            // En-têtes de démonstration : quelle instance a répondu, était-elle « froide », combien de temps a été facturé.
            http.Response.Headers["X-Instance"] = $"{nom}#{resultat.Instance}";
            http.Response.Headers["X-Demarrage-A-Froid"] = resultat.DemarrageAFroid ? "oui" : "non";
            http.Response.Headers["X-Duree-Ms"] = resultat.DureeMs.ToString();
            if (reponse.Location is not null)
                http.Response.Headers.Location = reponse.Location;

            return reponse.Corps is null ? Results.StatusCode(reponse.Statut) : Results.Json(reponse.Corps, statusCode: reponse.Statut);
        }
        catch (EtranglementException e)
        {
            return Results.Json(new { erreur = e.Message }, statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (DelaiDepasseException e)
        {
            return Results.Json(new { erreur = e.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception e)
        {
            return Results.Json(new { erreur = e.Message }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
