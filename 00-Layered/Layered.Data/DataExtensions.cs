using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Layered.Data;

public static class DataExtensions
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IServiceCollection AddDataLayer(this IServiceCollection services, string databaseName)
    {
        services.AddDbContext<BankDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IAccountsDao, AccountsDao>();
        return services;
    }

    public static async Task SeedDemoData(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dao = scope.ServiceProvider.GetRequiredService<IAccountsDao>();
        if (!await dao.IsEmpty())
            return;

        await dao.Add(new AccountEntity { Id = Alice, Owner = "Alice", Balance = 1000m });
        await dao.Add(new AccountEntity { Id = Bob, Owner = "Bob", Balance = 500m });
        await dao.Save();
    }
}
