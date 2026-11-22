using Simple.Scripts;

namespace Simple.Api;

/// <summary>Transaction Script: each route calls ONE script, which does everything. The route only translates to HTTP.</summary>
public static class ScriptEndpoints
{
    public static void Map(RouteGroupBuilder routes)
    {
        routes.MapPost("/accounts", (OpenAccountRequest request, BankScripts scripts) => Run(() =>
        {
            var id = scripts.OpenAccount(request.Owner, request.InitialDeposit);
            return Results.Created($"/scripts/accounts/{id}", scripts.GetAccount(id));
        }));

        routes.MapGet("/accounts/{id:guid}", (Guid id, BankScripts scripts) =>
            Run(() => Results.Ok(scripts.GetAccount(id))));

        routes.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest request, BankScripts scripts) =>
            Run(() => Results.Ok(scripts.Withdraw(id, request.Amount))));

        routes.MapPost("/transfers", (TransferRequest request, BankScripts scripts) =>
            Run(() => Results.Ok(scripts.Transfer(request.Source, request.Target, request.Amount))));
    }

    private static IResult Run(Func<IResult> script)
    {
        try
        {
            return script();
        }
        catch (AccountNotFoundException e)
        {
            return Results.NotFound(new { error = e.Message });
        }
        catch (ScriptRefusedException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}

public record OpenAccountRequest(string Owner, decimal InitialDeposit);
public record WithdrawalRequest(decimal Amount);
public record TransferRequest(Guid Source, Guid Target, decimal Amount);
