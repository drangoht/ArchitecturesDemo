using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Mt.Tenants;

namespace Mt.Bank;

public static class Setup
{
    public static IServiceCollection AddBank(this IServiceCollection services)
    {
        services.AddSingleton(new InMemoryDatabaseRoot());
        services.AddSingleton<BankFactory>();
        services.AddSingleton(TimeProvider.System);
        // One DbContext per request, opened on the database OF the request's tenant.
        services.AddScoped(sp => sp.GetRequiredService<BankFactory>().Open(sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<BankingService>();
        return services;
    }
}
