using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Read.Queries;

/// <summary>A question asked to the system. It changes nothing.</summary>
public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query);
}

/// <summary>Finds the handler of the query from its type.</summary>
public class QueryBus(IServiceProvider services)
{
    public Task<TResult> Ask<TResult>(IQuery<TResult> query)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = services.GetRequiredService(handlerType);
        return (Task<TResult>)handlerType.GetMethod("Handle")!.Invoke(handler, [query])!;
    }
}
