using Vertical.Api.Data;
using Vertical.Api.Domain;

namespace Vertical.Api.Features.Transfers;

public static class MakeTransfer
{
    public record Request(Guid Source, Guid Target, decimal Amount);
    public record Response(decimal SourceBalance, decimal TargetBalance);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/transfers", Execute);

    private static async Task<IResult> Execute(Request request, BankDbContext db)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var source = await db.Accounts.FindAsync(request.Source);
        var target = await db.Accounts.FindAsync(request.Target);
        if (source is null || target is null)
            return Results.NotFound();

        // Rule shared with WithdrawMoney → it lives in the Account entity.
        if (source.Withdraw(request.Amount) is { } refusal)
            return Results.BadRequest(new { error = refusal });
        target.Deposit(request.Amount);

        db.Operations.AddRange(
            new Operation { AccountId = source.Id, Label = $"Transfer to {target.Owner}", Amount = -request.Amount },
            new Operation { AccountId = target.Id, Label = $"Transfer from {source.Owner}", Amount = request.Amount });
        await db.SaveChangesAsync();

        return Results.Ok(new Response(source.Balance, target.Balance));
    }

    private static Dictionary<string, string[]> Validate(Request request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Amount <= 0)
            errors[nameof(Request.Amount)] = ["The amount must be strictly positive."];
        if (request.Source == request.Target)
            errors[nameof(Request.Target)] = ["Cannot transfer to the same account."];
        return errors;
    }
}
