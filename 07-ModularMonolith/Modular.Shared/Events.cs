using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modular.Shared;

/// <summary>Marker for the events a module publishes for the others.</summary>
public interface IIntegrationEvent;

public interface IEventBus
{
    Task Publish<T>(T @event) where T : IIntegrationEvent;
}

/// <summary>Implemented by a module that wants to react to another module's event.</summary>
public interface IEventHandler<in T> where T : IIntegrationEvent
{
    Task Handle(T @event);
}

/// <summary>
/// In-memory bus, in the same process. Each handler runs in its own scope,
/// and an error in a subscribed module does not make the publishing module fail.
/// In production: an "outbox" (event saved in the same transaction as the data)
/// then asynchronous processing, so that no event is lost.
/// </summary>
internal sealed class InMemoryBus(IServiceScopeFactory scopes, ILogger<InMemoryBus> logger) : IEventBus
{
    public async Task Publish<T>(T @event) where T : IIntegrationEvent
    {
        using var scope = scopes.CreateScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IEventHandler<T>>())
        {
            try
            {
                await handler.Handle(@event);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Handler {Handler} failed on {Event}",
                    handler.GetType().Name, typeof(T).Name);
            }
        }
    }
}

public static class BusExtensions
{
    public static IServiceCollection AddEventBus(this IServiceCollection services) =>
        services.AddSingleton<IEventBus, InMemoryBus>();
}
