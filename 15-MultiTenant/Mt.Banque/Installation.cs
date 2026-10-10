using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Mt.Locataires;

namespace Mt.Banque;

public static class Installation
{
    public static IServiceCollection AddBanque(this IServiceCollection services)
    {
        services.AddSingleton(new InMemoryDatabaseRoot());
        services.AddSingleton<FabriqueBanque>();
        services.AddSingleton(TimeProvider.System);
        // Un DbContext par requête, ouvert sur la base DU locataire de la requête.
        services.AddScoped(sp => sp.GetRequiredService<FabriqueBanque>().Ouvrir(sp.GetRequiredService<IContexteLocataire>()));
        services.AddScoped<ServiceBancaire>();
        return services;
    }
}
