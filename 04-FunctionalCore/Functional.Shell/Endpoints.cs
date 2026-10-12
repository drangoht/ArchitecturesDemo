using System.Diagnostics;
using Functional.Core;
using Functional.Shell.Data;
using Microsoft.EntityFrameworkCore;

namespace Functional.Shell;

public static class Endpoints
{
    public record WithdrawalRequest(decimal Amount);
    public record TransferRequest(Guid Source, Guid Target, decimal Amount);

    public static void MapBank(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", Get);
        app.MapPost("/accounts/{id:guid}/withdrawal", Withdraw);
        app.MapPost("/transfers", Transfer);
    }

    private static async Task<IResult> Get(Guid id, BankDbContext db)
    {
        var row = await db.Accounts.FindAsync(id);
        return row is null ? Results.NotFound() : Results.Ok(row.ToCore());
    }

    private static async Task<IResult> Withdraw(
        Guid id, WithdrawalRequest request, BankDbContext db, TimeProvider clock, ILoggerFactory loggers)
    {
        // 1. READ (I/O)
        var row = await db.Accounts.FindAsync(id);
        if (row is null)
            return Results.NotFound();

        // 2. DECIDE (pure)
        var decision = Rules.Withdraw(row.ToCore(), request.Amount);

        // 3. APPLY (I/O)
        switch (decision)
        {
            case WithdrawalDecision.Accepted ok:
                row.Balance = ok.UpdatedAccount.Balance;
                db.Operations.Add(Operation(id, "Withdrawal", ok.Amount, clock));
                await db.SaveChangesAsync();
                loggers.CreateLogger("Bank").LogInformation("Withdrawal of {Amount} € from {Account}", ok.Amount, id);
                return Results.Ok(ok.UpdatedAccount);

            case WithdrawalDecision.Refused refusal:
                return Results.BadRequest(new { error = refusal.Reason });

            default:
                throw new UnreachableException();
        }
    }

    private static async Task<IResult> Transfer(
        TransferRequest request, BankDbContext db, TimeProvider clock, ILoggerFactory loggers)
    {
        // 1. READ (I/O): the accounts AND everything the rule needs (here, the day's total).
        var source = await db.Accounts.FindAsync(request.Source);
        var target = await db.Accounts.FindAsync(request.Target);
        if (source is null || target is null)
            return Results.NotFound();

        var startOfDay = clock.GetUtcNow().UtcDateTime.Date;
        var alreadyTransferred = await db.Operations
            .Where(o => o.AccountId == source.Id && o.Type == "TransferSent" && o.DateUtc >= startOfDay)
            .SumAsync(o => o.Amount);

        // 2. DECIDE (pure)
        var decision = Rules.Transfer(source.ToCore(), target.ToCore(), request.Amount, alreadyTransferred);

        // 3. APPLY (I/O)
        switch (decision)
        {
            case TransferDecision.Accepted ok:
                source.Balance = ok.Source.Balance;
                target.Balance = ok.Target.Balance;
                db.Operations.Add(Operation(source.Id, "TransferSent", ok.Amount, clock));
                db.Operations.Add(Operation(target.Id, "TransferReceived", ok.Amount, clock));
                await db.SaveChangesAsync();
                loggers.CreateLogger("Bank").LogInformation(
                    "Transfer of {Amount} € from {Source} to {Target}", ok.Amount, source.Id, target.Id);
                return Results.Ok(new { source = ok.Source, target = ok.Target });

            case TransferDecision.Refused refusal:
                return Results.BadRequest(new { error = refusal.Reason });

            default:
                throw new UnreachableException();
        }
    }

    private static OperationRow Operation(Guid accountId, string type, decimal amount, TimeProvider clock) =>
        new() { AccountId = accountId, Type = type, Amount = amount, DateUtc = clock.GetUtcNow().UtcDateTime };
}
