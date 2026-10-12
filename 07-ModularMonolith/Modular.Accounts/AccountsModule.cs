using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modular.Accounts.Contracts;
using Modular.Shared;

namespace Modular.Accounts;

public sealed class AccountsModule : IModule
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public string Name => "Accounts";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AccountsDbContext>((sp, options) => options.UseInMemoryDatabase(sp.DatabaseName(Name)));
        services.AddScoped<IAccountsApi, AccountsApi>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => AccountsEndpoints.Map(app);

    public async Task Initialize(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        if (await db.Accounts.AnyAsync())
            return;
        db.Accounts.AddRange(new Account(Alice, "Alice", 1000m), new Account(Bob, "Bob", 500m));
        await db.SaveChangesAsync();
    }
}
