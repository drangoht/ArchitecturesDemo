using Med.Mediator;

namespace Med.Bank.Pipeline;

// The mediator only knows "request". Command and query are OUR labels, used by the UnitOfWorkBehavior
// to save after a command and never after a query. Same database, same model: this is not CQRS.
public interface ICommand<TResponse> : IRequest<TResponse>;

public interface IQuery<TResponse> : IRequest<TResponse>;

public record ValidationError(string Field, string Message);

/// <summary>The rules on the shape of a request (required fields, ranges), checked before its handler.</summary>
public interface IValidator<in TRequest>
{
    IEnumerable<ValidationError> Validate(TRequest request);
}

public class RequestValidationException(IReadOnlyList<ValidationError> errors)
    : Exception($"{errors.Count} validation error(s).")
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
