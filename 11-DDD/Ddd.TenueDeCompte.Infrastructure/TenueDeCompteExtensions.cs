using Ddd.TenueDeCompte.Application;
using Ddd.TenueDeCompte.Contrats;
using Ddd.TenueDeCompte.Domaine.Comptes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ddd.TenueDeCompte.Infrastructure;

public static class TenueDeCompteExtensions
{
    public static IServiceCollection AddTenueDeCompte(this IServiceCollection services, Func<IServiceProvider, string> nomBase)
    {
        services.AddDbContext<TenueDeCompteDb>((sp, options) => options.UseInMemoryDatabase(nomBase(sp)));
        services.AddScoped<IUniteDeTravail>(sp => sp.GetRequiredService<TenueDeCompteDb>());
        services.AddScoped<IComptes, DepotComptes>();

        services.AddSingleton<SystemeDeCotationsHerite>();
        services.AddSingleton<IConvertisseurDevises, AdaptateurTauxDeChange>();

        services.AddScoped<FabriqueDeComptes>();
        services.AddScoped<ServiceDeVirement>();
        services.AddScoped(sp => new PublicateurEvenements(
            () => sp.GetServices<IAbonneTenueDeCompte>(), sp.GetRequiredService<IConvertisseurDevises>()));
        services.AddScoped<ServiceComptes>();
        services.AddScoped<ITenueDeCompteApi, TenueDeCompteApi>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
