using Hexa.Hexagon.Domain;
using Hexa.Hexagon.Ports.Inbound;

namespace Hexa.Adapters.Web;

/// <summary>
/// Inbound HTTP adapter: translates HTTP ⇄ inbound ports. No business rule here.
/// </summary>
public static class AccountsEndpoints
{
    public record WithdrawalRequest(decimal Amount);
    public record TransferRequest(Guid Source, Guid Target, decimal Amount);

    public static void MapAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", (Guid id, IGetAccount get) =>
            Translate(async () => Results.Ok(await get.Execute(id))));

        app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest request, IWithdrawMoney withdraw) =>
            Translate(async () =>
            {
                await withdraw.Execute(id, request.Amount);
                return Results.NoContent();
            }));

        app.MapPost("/transfers", (TransferRequest request, IMakeTransfer transfer) =>
            Translate(async () =>
            {
                await transfer.Execute(request.Source, request.Target, request.Amount);
                return Results.NoContent();
            }));
    }

    // Translating business errors into HTTP codes is the adapter's job, not the hexagon's.
    private static async Task<IResult> Translate(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AccountNotFoundException e)
        {
            return Results.NotFound(new { error = e.Message });
        }
        catch (BusinessRuleException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}
