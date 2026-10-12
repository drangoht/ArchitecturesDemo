using Cqrs.Write.Commands;
using Cqrs.Write.Persistence;
using Cqrs.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Write;

public static class WriteExtensions
{
    public static IServiceCollection AddWriteSide(this IServiceCollection services, Func<IServiceProvider, string> databaseName)
    {
        services.AddDbContext<WriteDb>((sp, options) => options.UseInMemoryDatabase(databaseName(sp)));
        services.AddScoped<IEventStream, EventStream>();
        services.AddScoped<CommandBus>();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IValidator<OpenAccount>, OpenAccountValidator>();
        services.AddScoped<ICommandHandler<OpenAccount>, OpenAccountHandler>();
        services.AddScoped<IValidator<WithdrawMoney>, WithdrawMoneyValidator>();
        services.AddScoped<ICommandHandler<WithdrawMoney>, WithdrawMoneyHandler>();
        services.AddScoped<IValidator<MakeTransfer>, MakeTransferValidator>();
        services.AddScoped<ICommandHandler<MakeTransfer>, MakeTransferHandler>();
        return services;
    }
}
