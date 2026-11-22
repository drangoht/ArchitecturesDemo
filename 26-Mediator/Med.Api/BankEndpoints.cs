using Med.Bank.Domain;
using Med.Bank.Features.Accounts;
using Med.Bank.Features.Transfers;
using Med.Bank.Pipeline;
using Med.Mediator;

namespace Med.Api;

/// <summary>
/// Every route has the same shape: build a message, Send it, translate the outcome into HTTP.
/// The routes only know IMediator; which handler answers, and what runs around it, they cannot see.
/// </summary>
public static class BankEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts", (OpenAccount command, IMediator mediator, CancellationToken ct) => Run(async () =>
        {
            var id = await mediator.Send(command, ct);
            return Results.Created($"/accounts/{id}", await mediator.Send(new GetAccount(id), ct));
        }));

        app.MapGet("/accounts/{id:guid}", (Guid id, IMediator mediator, CancellationToken ct) =>
            Run(async () => Results.Ok(await mediator.Send(new GetAccount(id), ct))));

        app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest request, IMediator mediator, CancellationToken ct) =>
            Run(async () => Results.Ok(new { NewBalance = await mediator.Send(new Withdraw(id, request.Amount), ct) })));

        app.MapPost("/transfers", (Transfer command, IMediator mediator, CancellationToken ct) =>
            Run(async () => Results.Ok(await mediator.Send(command, ct))));

        app.MapGet("/compliance/alerts", (IMediator mediator, CancellationToken ct) =>
            Run(async () => Results.Ok(await mediator.Send(new ListComplianceAlerts(), ct))));

        // Not a request: the journal is read directly, to show what the JournalBehavior recorded.
        app.MapGet("/journal", (RequestJournal journal) => Results.Ok(journal.Latest(20)));
    }

    private static async Task<IResult> Run(Func<Task<IResult>> send)
    {
        try
        {
            return await send();
        }
        catch (RequestValidationException e)
        {
            return Results.ValidationProblem(e.Errors
                .GroupBy(error => error.Field)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));
        }
        catch (NotFoundException e)
        {
            return Results.NotFound(new { error = e.Message });
        }
        catch (BusinessRuleException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}

public record WithdrawalRequest(decimal Amount);
