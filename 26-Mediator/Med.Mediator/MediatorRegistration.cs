using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Mediator;

public static class MediatorRegistration
{
    private static readonly Type[] HandlerContracts = [typeof(IRequestHandler<,>), typeof(INotificationHandler<>)];

    /// <summary>Registers the mediator and every handler found in the assemblies (what MediatR's scan does).</summary>
    public static MediatorBuilder AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IMediator, Mediator>();

        var concreteTypes = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });
        foreach (var type in concreteTypes)
            foreach (var contract in type.GetInterfaces())
                if (contract.IsGenericType && HandlerContracts.Contains(contract.GetGenericTypeDefinition()))
                    services.AddScoped(contract, type);

        return new MediatorBuilder(services);
    }
}

public sealed class MediatorBuilder(IServiceCollection services)
{
    /// <summary>An open generic behavior, applied to every request. The order of the calls is the order of the pipeline.</summary>
    public MediatorBuilder AddBehavior(Type openBehaviorType)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), openBehaviorType);
        return this;
    }
}
