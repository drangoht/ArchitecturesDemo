using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Components.Accounts;

/// <summary>The only other public point: plugging the component into dependency injection.</summary>
public static class AccountsExtensions
{
    public static IServiceCollection AddAccountsComponent(this IServiceCollection services, string databaseName = "Accounts")
    {
        // The component owns ITS database: no other component accesses it.
        services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IAccountsComponent, AccountsComponent>();
        return services;
    }
}
