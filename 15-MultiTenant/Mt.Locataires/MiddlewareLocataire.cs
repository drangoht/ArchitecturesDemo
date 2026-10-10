using Microsoft.AspNetCore.Http;

namespace Mt.Locataires;

/// <summary>
/// Le point d'entrée de la location : identifie le locataire AVANT tout le reste (limitation de débit, métier, données)
/// et le range dans le <see cref="ContexteLocataire"/> de la requête. Une requête sans locataire valide n'atteint jamais le métier.
/// </summary>
public sealed class MiddlewareLocataire(
    RequestDelegate suite, IEnumerable<IResolveurLocataire> resolveurs, CatalogueLocataires catalogue, CheminsHorsLocataire horsLocataire)
{
    public async Task InvokeAsync(HttpContext http, ContexteLocataire contexte)
    {
        if (horsLocataire.Chemins.Any(c => http.Request.Path.StartsWithSegments(c)))
        {
            await suite(http);
            return;
        }

        var trouves = resolveurs
            .Select(r => (r.Nom, Id: r.Resoudre(http)))
            .Where(r => r.Id is not null)
            .ToList();
        var identifiants = trouves.Select(r => r.Id!.ToLowerInvariant()).Distinct().ToList();

        if (identifiants.Count == 0)
        {
            await Refuser(http, StatusCodes.Status400BadRequest,
                $"Locataire non précisé : utiliser un sous-domaine (alpha.localhost) ou l'en-tête {ResolveurParEnTete.EnTete}.");
            return;
        }
        if (identifiants.Count > 1)
        {
            // Sous-domaine « alpha » et en-tête « beta » : on ne choisit pas, on refuse.
            await Refuser(http, StatusCodes.Status400BadRequest,
                "Locataire ambigu : " + string.Join(", ", trouves.Select(r => $"{r.Nom} = {r.Id}")));
            return;
        }

        var locataire = catalogue.Trouver(identifiants[0]);
        if (locataire is null)
        {
            await Refuser(http, StatusCodes.Status404NotFound, $"Locataire inconnu : « {identifiants[0]} ».");
            return;
        }
        if (!locataire.Actif)
        {
            await Refuser(http, StatusCodes.Status403Forbidden, $"Le locataire « {locataire.Nom} » est suspendu.");
            return;
        }

        contexte.Definir(locataire);
        http.Response.Headers[ResolveurParEnTete.EnTete] = locataire.Id;
        await suite(http);
    }

    private static Task Refuser(HttpContext http, int statut, string erreur)
    {
        http.Response.StatusCode = statut;
        return http.Response.WriteAsJsonAsync(new { erreur });
    }
}

/// <summary>Les routes qui s'exécutent sans locataire (le back-office de la plateforme).</summary>
public sealed record CheminsHorsLocataire(IReadOnlyList<string> Chemins);
