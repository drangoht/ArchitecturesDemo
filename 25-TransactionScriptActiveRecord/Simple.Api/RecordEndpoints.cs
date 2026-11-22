using Simple.Data;
using Simple.Records;

namespace Simple.Api;

/// <summary>
/// Active Record: the route opens the transaction, finds the objects, calls their rules, saves them and commits.
/// With nothing between the route and the records, the coordination lands here.
/// </summary>
public static class RecordEndpoints
{
    public static void Map(RouteGroupBuilder routes)
    {
        routes.MapPost("/accounts", (OpenAccountRequest request, Database database) => Run(() =>
        {
            using var tx = database.Begin();
            var account = Account.Open(tx, request.Owner, request.InitialDeposit);
            account.Save();
            var response = ToResponse(account);
            tx.Commit();
            return Results.Created($"/records/accounts/{account.Id}", response);
        }));

        routes.MapGet("/accounts/{id:guid}", (Guid id, Database database) =>
        {
            using var tx = database.Begin();
            return Account.Find(tx, id) is { } account ? Results.Ok(ToResponse(account)) : NotFound(id);
        });

        routes.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest request, Database database) => Run(() =>
        {
            using var tx = database.Begin();
            if (Account.Find(tx, id) is not { } account)
                return NotFound(id);

            account.Withdraw(request.Amount);
            account.Save();
            tx.Commit();
            return Results.Ok(new { AccountId = account.Id, NewBalance = account.Balance });
        }));

        routes.MapPost("/transfers", (TransferRequest request, Database database) => Run(() =>
        {
            using var tx = database.Begin();
            if (Account.Find(tx, request.Source) is not { } source)
                return NotFound(request.Source);
            if (Account.Find(tx, request.Target) is not { } target)
                return NotFound(request.Target);

            source.TransferTo(target, request.Amount);
            source.Save();
            target.Save();
            tx.Commit();
            return Results.Ok(new { SourceBalance = source.Balance, TargetBalance = target.Balance });
        }));
    }

    private static object ToResponse(Account account) => new
    {
        account.Id,
        account.Owner,
        account.Balance,
        LatestOperations = account.LatestOperations().Select(o => new { o.Label, o.Amount, o.DateUtc })
    };

    private static IResult NotFound(Guid id) => Results.NotFound(new { error = $"Account {id} not found." });

    private static IResult Run(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (RuleViolationException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}
