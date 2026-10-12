using Vertical.Api.Data;
using Vertical.Api.Domain;

namespace Vertical.Api.Features.Accounts;

/// <summary>
/// Write slice: route + validation + logic + persistence, in a single file.
/// To understand or change "withdraw money", this is the only file to open.
/// </summary>
public static class WithdrawMoney
{
    public record Request(decimal Amount);
    public record Response(Guid AccountId, decimal NewBalance);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/accounts/{id:guid}/withdrawal", Execute);

    private static async Task<IResult> Execute(Guid id, Request request, BankDbContext db)
    {
        if (request.Amount <= 0)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(Request.Amount)] = ["The amount must be strictly positive."]
            });

        var account = await db.Accounts.FindAsync(id);
        if (account is null)
            return Results.NotFound();

        if (account.Withdraw(request.Amount) is { } refusal)
            return Results.BadRequest(new { error = refusal });

        db.Operations.Add(new Operation { AccountId = id, Label = "Withdrawal", Amount = -request.Amount });
        await db.SaveChangesAsync();

        return Results.Ok(new Response(id, account.Balance));
    }
}
