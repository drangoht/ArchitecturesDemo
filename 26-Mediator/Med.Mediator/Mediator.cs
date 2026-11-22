using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Mediator;

/// <summary>
/// The mediator: it finds the handler of a request in the container, wraps it in the behaviors, and calls it.
/// It holds no business logic and knows no request: everything is discovered at run time.
/// </summary>
public sealed class Mediator(IServiceProvider services) : IMediator
{
    // One dispatcher per request type, created by reflection the first time, then reused.
    private static readonly ConcurrentDictionary<Type, object> Dispatchers = new();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The caller only knows TResponse; the request's real type is only known now.
        // The dispatcher closes the generic types once: IRequestHandler<Withdraw, decimal>, and so on.
        var dispatcher = (Dispatcher<TResponse>)Dispatchers.GetOrAdd(request.GetType(), type =>
            Activator.CreateInstance(typeof(Dispatcher<,>).MakeGenericType(type, typeof(TResponse)))!);
        return dispatcher.Send(request, services, ct);
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        // One after the other, in the order of registration, in the caller's flow:
        // the first exception stops the others and goes back up to the caller.
        foreach (var handler in services.GetServices<INotificationHandler<TNotification>>())
            await handler.Handle(notification, ct);
    }

    private abstract class Dispatcher<TResponse>
    {
        public abstract Task<TResponse> Send(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct);
    }

    private sealed class Dispatcher<TRequest, TResponse> : Dispatcher<TResponse> where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> Send(IRequest<TResponse> request, IServiceProvider services, CancellationToken ct)
        {
            var typed = (TRequest)request;
            var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
                ?? throw new InvalidOperationException($"No handler is registered for {typeof(TRequest).Name}.");

            // The behaviors wrap the handler like the layers of an onion:
            // the first one registered is the outermost, the handler is at the center.
            RequestHandlerDelegate<TResponse> next = () => handler.Handle(typed, ct);
            foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResponse>>().Reverse())
            {
                var inner = next;
                next = () => behavior.Handle(typed, inner, ct);
            }
            return next();
        }
    }
}
