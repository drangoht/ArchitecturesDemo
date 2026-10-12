using Ddd.AccountKeeping.Application;
using Ddd.AccountKeeping.Contracts;
using Ddd.AccountKeeping.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ddd.AccountKeeping.Infrastructure;

public static class AccountKeepingExtensions
{
    public static IServiceCollection AddAccountKeeping(this IServiceCollection services, Func<IServiceProvider, string> databaseName)
    {
        services.AddDbContext<AccountKeepingDb>((sp, options) => options.UseInMemoryDatabase(databaseName(sp)));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AccountKeepingDb>());
        services.AddScoped<IAccounts, AccountRepository>();

        services.AddSingleton<LegacyQuoteSystem>();
        services.AddSingleton<ICurrencyConverter, ExchangeRateAdapter>();

        services.AddScoped<AccountFactory>();
        services.AddScoped<TransferService>();
        services.AddScoped(sp => new EventPublisher(
            () => sp.GetServices<IAccountKeepingSubscriber>(), sp.GetRequiredService<ICurrencyConverter>()));
        services.AddScoped<AccountsService>();
        services.AddScoped<IAccountKeepingApi, AccountKeepingApi>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
