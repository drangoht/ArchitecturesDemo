using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modular.Accounts.Contracts;
using Modular.Shared;
using Modular.Transfers.Contracts;

namespace Modular.Transfers;

internal static class TransfersEndpoints
{
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);

    public static void Map(IEndpointRouteBuilder app)
    {
        var transfers = app.MapGroup("/transfers");
        transfers.MapPost("", Transfer);
        transfers.MapGet("/account/{id:guid}", History);
    }

    private static async Task<IResult> Transfer(
        TransferRequest request, IAccountsApi accounts, TransfersDbContext db, IEventBus bus)
    {
        if (request.Amount <= 0)
            return Results.BadRequest(new { error = "The amount must be strictly positive." });
        if (request.Source == request.Target)
            return Results.BadRequest(new { error = "Cannot transfer to the same account." });

        // Synchronous call to another module, only through its public contract.
        var source = await accounts.Get(request.Source);
        var target = await accounts.Get(request.Target);
        if (source is null || target is null)
            return Results.NotFound(new { error = "Source or target account not found." });

        var debit = await accounts.Debit(source.Id, request.Amount);
        if (!debit.Success)
            return Results.BadRequest(new { error = debit.Error });

        var credit = await accounts.Credit(target.Id, request.Amount);
        if (!credit.Success)
        {
            // Each module has its own database: no shared transaction, so the debit is compensated.
            await accounts.Credit(source.Id, request.Amount);
            return Results.BadRequest(new { error = credit.Error });
        }

        db.Transfers.Add(new Transfer { SourceId = source.Id, TargetId = target.Id, Amount = request.Amount });
        await db.SaveChangesAsync();

        // Asynchronous communication: the transfer is announced to whoever wants to hear it.
        await bus.Publish(new TransferMade(source.Id, source.Owner, target.Id, target.Owner, request.Amount));

        return Results.Ok(new { amount = request.Amount, from = source.Owner, to = target.Owner });
    }

    private static async Task<IResult> History(Guid id, TransfersDbContext db) =>
        Results.Ok(await db.Transfers
            .Where(v => v.SourceId == id || v.TargetId == id)
            .OrderByDescending(v => v.DateUtc)
            .Select(v => new { v.SourceId, v.TargetId, v.Amount, v.DateUtc })
            .ToListAsync());
}
