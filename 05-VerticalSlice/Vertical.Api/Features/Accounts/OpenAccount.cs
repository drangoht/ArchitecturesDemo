using Vertical.Api.Data;
using Vertical.Api.Domain;

namespace Vertical.Api.Features.Accounts;

/// <summary>
/// The simplest possible slice ("transaction script"):
/// no shared rule, so everything fits here, without abstraction.
/// </summary>
public static class OpenAccount
{
    public record Request(string Owner, decimal InitialDeposit);
    public record Response(Guid Id, string Owner, decimal Balance);

    public static void Map(IEndpointRouteBuilder app) => app.MapPost("/accounts", Execute);

    private static async Task<IResult> Execute(Request request, BankDbContext db)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var account = new Account(Guid.NewGuid(), request.Owner.Trim(), request.InitialDeposit);
        db.Accounts.Add(account);
        db.Operations.Add(new Operation { AccountId = account.Id, Label = "Initial deposit", Amount = request.InitialDeposit });
        await db.SaveChangesAsync();

        return Results.Created($"/accounts/{account.Id}", new Response(account.Id, account.Owner, account.Balance));
    }

    private static Dictionary<string, string[]> Validate(Request request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Owner))
            errors[nameof(Request.Owner)] = ["The owner is required."];
        if (request.InitialDeposit < 10)
            errors[nameof(Request.InitialDeposit)] = ["The initial deposit must be at least 10 €."];
        return errors;
    }
}
