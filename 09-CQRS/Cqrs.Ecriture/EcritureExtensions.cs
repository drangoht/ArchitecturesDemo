using Cqrs.Ecriture.Commandes;
using Cqrs.Ecriture.Persistance;
using Cqrs.Evenements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Ecriture;

public static class EcritureExtensions
{
    public static IServiceCollection AddCoteEcriture(this IServiceCollection services, Func<IServiceProvider, string> nomBase)
    {
        services.AddDbContext<EcritureDb>((sp, options) => options.UseInMemoryDatabase(nomBase(sp)));
        services.AddScoped<IFluxEvenements, FluxEvenements>();
        services.AddScoped<BusCommandes>();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IValidateur<OuvrirCompte>, OuvrirCompteValidateur>();
        services.AddScoped<IGestionnaireCommande<OuvrirCompte>, OuvrirCompteGestionnaire>();
        services.AddScoped<IValidateur<RetirerArgent>, RetirerArgentValidateur>();
        services.AddScoped<IGestionnaireCommande<RetirerArgent>, RetirerArgentGestionnaire>();
        services.AddScoped<IValidateur<EffectuerVirement>, EffectuerVirementValidateur>();
        services.AddScoped<IGestionnaireCommande<EffectuerVirement>, EffectuerVirementGestionnaire>();
        return services;
    }
}
