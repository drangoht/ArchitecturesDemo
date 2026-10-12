using Polly;

namespace Micro.Transfers;

internal static class Endpoints
{
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);

    public static void MapTransfers(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transfers", Transfer);
        app.MapGet("/transfers/{id:guid}", async (Guid id, TransfersDb db) =>
            await db.Transfers.FindAsync(id) is { } v ? Results.Ok(View(v)) : Results.NotFound());
    }

    private static async Task<IResult> Transfer(TransferRequest request, AccountsClient accounts, TransfersDb db)
    {
        if (request.Amount <= 0)
            return Results.BadRequest(new { error = "The amount must be strictly positive." });
        if (request.Source == request.Target)
            return Results.BadRequest(new { error = "Cannot transfer to the same account." });

        var transfer = new Transfer { SourceId = request.Source, TargetId = request.Target, Amount = request.Amount };
        db.Transfers.Add(transfer);
        await db.SaveChangesAsync();

        // Step 1: check the accounts and debit the source.
        RemoteAccount? source, target;
        try
        {
            source = await accounts.Get(request.Source);
            target = await accounts.Get(request.Target);
            if (source is null || target is null)
                return await Finish(db, transfer, TransferStatus.Refused, "Source or target account not found.", 404);

            var debit = await accounts.Debit(source.Id, request.Amount, $"{transfer.Id}-debit");
            if (debit.Outcome != Outcome.Accepted)
                return await Finish(db, transfer, TransferStatus.Refused, debit.Error, 400);
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            // The debit may have been applied (lost response). Thanks to the operationId,
            // a resumption can replay the call without risk of a double debit.
            return await Finish(db, transfer, TransferStatus.ToResume, "Accounts service unavailable.", 503);
        }

        // Step 2: credit the target. On failure: compensation (the source is credited back).
        string? creditFailure;
        try
        {
            var credit = await accounts.Credit(target.Id, request.Amount, $"{transfer.Id}-credit");
            creditFailure = credit.Outcome == Outcome.Accepted ? null : credit.Error;
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            creditFailure = "Accounts service unavailable.";
        }

        if (creditFailure is not null)
        {
            try
            {
                await accounts.Credit(source.Id, request.Amount, $"{transfer.Id}-compensation");
                return await Finish(db, transfer, TransferStatus.Cancelled, $"{creditFailure} Debit cancelled.", 400);
            }
            catch (Exception e) when (IsNetworkFailure(e))
            {
                return await Finish(db, transfer, TransferStatus.ToResume,
                    "Credit and compensation impossible: transfer to resume.", 503);
            }
        }

        // Step 3: success. Status AND event saved in the same transaction (outbox).
        db.Outbox.Add(OutboxMessage.Create("transfer-made", new
        {
            transferId = transfer.Id,
            sourceId = source.Id,
            sourceOwner = source.Owner,
            targetId = target.Id,
            targetOwner = target.Owner,
            amount = request.Amount
        }));
        return await Finish(db, transfer, TransferStatus.Done, null, 200);
    }

    private static bool IsNetworkFailure(Exception e) =>
        e is HttpRequestException or TaskCanceledException or ExecutionRejectedException;

    private static async Task<IResult> Finish(
        TransfersDb db, Transfer transfer, TransferStatus status, string? error, int httpCode)
    {
        transfer.Status = status;
        transfer.Error = error;
        await db.SaveChangesAsync();
        return Results.Json(View(transfer), statusCode: httpCode);
    }

    private static object View(Transfer v) =>
        new { transferId = v.Id, status = v.Status.ToString(), v.Amount, error = v.Error };
}
