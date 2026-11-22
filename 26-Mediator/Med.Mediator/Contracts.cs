namespace Med.Mediator;

// The whole vocabulary of a mediator in the style of MediatR: five interfaces and a delegate.

/// <summary>A message that expects exactly one answer, from exactly one handler.</summary>
public interface IRequest<TResponse>;

/// <summary>A message announced to everyone interested: zero, one or several handlers, no answer.</summary>
public interface INotification;

public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface INotificationHandler<in TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken ct);
}

/// <summary>The rest of the pipeline: the next behavior, or the handler itself.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>Runs around every request: before the handler, after it, or instead of it.</summary>
public interface IPipelineBehavior<in TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>The only thing the callers know: they send a message, without knowing who handles it.</summary>
public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);

    Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : INotification;
}
