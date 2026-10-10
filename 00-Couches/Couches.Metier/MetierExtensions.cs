using Couches.Donnees;
using Microsoft.Extensions.DependencyInjection;

namespace Couches.Metier;

/// <summary>
/// La couche métier branche elle-même la couche du dessous :
/// ainsi la présentation ne parle qu'à la couche juste en dessous d'elle (couches « strictes »).
/// </summary>
public static class MetierExtensions
{
    public static IServiceCollection AddCoucheMetier(this IServiceCollection services, string nomBase = "Banque")
    {
        services.AddCoucheDonnees(nomBase);
        services.AddScoped<ComptesService>();
        return services;
    }

    public static Task PreparerDonneesDeDemo(this IServiceProvider services) =>
        services.InitialiserDonneesDeDemo();
}
