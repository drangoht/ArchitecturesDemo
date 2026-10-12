namespace Micro.Accounts;

internal static class Endpoints
{
    internal record WithdrawalRequest(decimal Amount);
    internal record MovementRequest(string OperationId, decimal Amount);

    /// <summary>Public API, exposed to the clients by the gateway.</summary>
    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", async (Guid id, AccountsDb db) =>
            await db.Accounts.FindAsync(id) is { } c
                ? Results.Ok(new { c.Id, c.Owner, c.Balance })
                : Results.NotFound(new { error = $"Account {id} not found." }));

        app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, WithdrawalRequest request, AccountsDb db) =>
        {
            var account = await db.Accounts.FindAsync(id);
            if (account is null)
                return Results.NotFound(new { error = $"Account {id} not found." });
            if (account.Debit(request.Amount) is { } refusal)
                return Results.BadRequest(new { error = refusal });

            // Change AND event saved together: either both, or neither.
            db.Outbox.Add(OutboxMessage.Create("withdrawal-made",
                new { accountId = id, amount = request.Amount, newBalance = account.Balance }));
            await db.SaveChangesAsync();

            return Results.Ok(new { account.Id, account.Owner, account.Balance });
        });
    }

    /// <summary>
    /// Internal API, called by the other services (the gateway does not expose it).
    /// Idempotent: a call replayed with the same operationId (retry after a network cut) does not debit twice.
    /// </summary>
    public static void MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var @internal = app.MapGroup("/internal/accounts/{id:guid}");
        @internal.MapPost("/debits", (Guid id, MovementRequest r, AccountsDb db) => Apply(id, r, db, (c, m) => c.Debit(m)));
        @internal.MapPost("/credits", (Guid id, MovementRequest r, AccountsDb db) => Apply(id, r, db, (c, m) => c.Credit(m)));
    }

    private static async Task<IResult> Apply(
        Guid id, MovementRequest request, AccountsDb db, Func<Account, decimal, string?> movement)
    {
        if (await db.ProcessedOperations.FindAsync(request.OperationId) is { } alreadyProcessed)
            return Respond(alreadyProcessed);

        var account = await db.Accounts.FindAsync(id);
        if (account is null)
            return Results.NotFound(new { error = $"Account {id} not found." });

        var refusal = movement(account, request.Amount);
        var operation = new ProcessedOperation
        {
            OperationId = request.OperationId,
            Accepted = refusal is null,
            Error = refusal,
            BalanceAfter = account.Balance
        };
        db.ProcessedOperations.Add(operation);
        await db.SaveChangesAsync();
        return Respond(operation);
    }

    private static IResult Respond(ProcessedOperation operation) => operation.Accepted
        ? Results.Ok(new { operation.OperationId, operation.BalanceAfter })
        : Results.BadRequest(new { error = operation.Error });
}
