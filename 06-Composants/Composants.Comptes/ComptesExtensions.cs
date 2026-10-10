using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Composants.Comptes;

/// <summary>Seul autre point public : le branchement du composant dans l'injection de dépendances.</summary>
public static class ComptesExtensions
{
    public static IServiceCollection AddComposantComptes(this IServiceCollection services, string nomBase = "Comptes")
    {
        // Le composant possède SA base : aucun autre composant n'y accède.
        services.AddDbContext<ComptesDbContext>(options => options.UseInMemoryDatabase(nomBase));
        services.AddScoped<IComptesComposant, ComptesComposant>();
        return services;
    }
}
