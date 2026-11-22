using System.Diagnostics;
using Med.Bank.Data;
using Med.Mediator;

namespace Med.Bank.Pipeline;

// The three behaviors, registered in this order (outermost first) in BankRegistration:
// Journal → Validation → UnitOfWork → handler. Written once, they apply to every request.

/// <summary>Writes every request in the journal: name, outcome, duration. Outermost, it also sees the refusals.</summary>
public class JournalBehavior<TRequest, TResponse>(RequestJournal journal) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            journal.Add(typeof(TRequest).Name, "ok", watch.Elapsed);
            return response;
        }
        catch (Exception e)
        {
            journal.Add(typeof(TRequest).Name, e.GetType().Name, watch.Elapsed);
            throw;
        }
    }
}

/// <summary>Runs the request's validators; if one finds an error, the handler is never called.</summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var errors = validators.SelectMany(v => v.Validate(request)).ToList();
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
        return next();
    }
}

/// <summary>
/// After a command, and only if its handler (and the notification handlers it published to) succeeded:
/// one SaveChanges for everything. After a query: nothing.
/// </summary>
public class UnitOfWorkBehavior<TRequest, TResponse>(BankDb db) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ICommand<TResponse>)
            return await next();

        var response = await next();
        await db.SaveChangesAsync(ct);
        return response;
    }
}

/// <summary>The latest requests handled, in memory (a stand-in for logs or traces).</summary>
public class RequestJournal
{
    private readonly Lock _gate = new();
    private readonly List<JournalEntry> _entries = [];

    public void Add(string request, string outcome, TimeSpan elapsed)
    {
        lock (_gate)
            _entries.Add(new JournalEntry(request, outcome, Math.Round(elapsed.TotalMilliseconds, 2)));
    }

    public IReadOnlyList<JournalEntry> Latest(int count)
    {
        lock (_gate)
            return [.. _entries.TakeLast(count)];
    }
}

public record JournalEntry(string Request, string Outcome, double ElapsedMs);
