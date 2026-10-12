using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Infrastructure;

public static class DependencyInjection
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string databaseName = "Bank")
    {
        services.AddDbContext<BankDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BankDbContext>());
        services.AddScoped<IAccountsRepository, AccountsRepository>();
        return services;
    }

    public static async Task SeedDemoData(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountsRepository>();
        if (!await accounts.IsEmpty())
            return;

        await accounts.Add(new Account(Alice, "Alice", 1000m));
        await accounts.Add(new Account(Bob, "Bob", 500m));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Save();
    }
}
