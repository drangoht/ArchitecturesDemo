using Med.Bank.Data;
using Med.Bank.Pipeline;
using Med.Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Bank;

public static class BankRegistration
{
    /// <param name="databaseName">Read when the database is created: each integration test gives its own.</param>
    public static IServiceCollection AddBank(this IServiceCollection services, Func<IServiceProvider, string> databaseName)
    {
        services.AddDbContext<BankDb>((provider, options) => options.UseInMemoryDatabase(databaseName(provider)));
        services.AddSingleton<RequestJournal>();

        // Handlers and notification handlers are found by scanning; the behaviors are listed, in pipeline order.
        services.AddMediator(typeof(BankRegistration).Assembly)
            .AddBehavior(typeof(JournalBehavior<,>))
            .AddBehavior(typeof(ValidationBehavior<,>))
            .AddBehavior(typeof(UnitOfWorkBehavior<,>));

        foreach (var type in typeof(BankRegistration).Assembly.GetTypes().Where(t => !t.IsAbstract))
            foreach (var contract in type.GetInterfaces())
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
                    services.AddScoped(contract, type);

        return services;
    }
}
