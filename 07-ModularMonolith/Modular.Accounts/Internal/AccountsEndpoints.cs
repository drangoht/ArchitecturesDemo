using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modular.Accounts.Contracts;
using Modular.Shared;

namespace Modular.Accounts;

/// <summary>The HTTP API belongs to the module (unlike 06-Components, where the web is a separate layer).</summary>
internal static class AccountsEndpoints
{
    internal record WithdrawalRequest(decimal Amount);

    public static void Map(IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/accounts");

        accounts.MapGet("/{id:guid}", async (Guid id, IAccountsApi api) =>
            await api.Get(id) is { } account ? Results.Ok(account) : Results.NotFound());

        accounts.MapPost("/{id:guid}/withdrawal", async (Guid id, WithdrawalRequest request, IAccountsApi api, IEventBus bus) =>
        {
            var result = await api.Debit(id, request.Amount);
            if (!result.Success)
                return ToError(result);

            var account = (await api.Get(id))!;
            // The module announces what happened; it does not know who is listening (here: Notifications).
            await bus.Publish(new WithdrawalMade(id, request.Amount, account.Balance));
            return Results.Ok(account);
        });
    }

    private static IResult ToError(OperationResult result) => result.Status == OperationStatus.NotFound
        ? Results.NotFound(new { error = result.Error })
        : Results.BadRequest(new { error = result.Error });
}
