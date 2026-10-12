using Components.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Components.Transfers;

internal class TransfersComponent(IAccountsComponent accounts, TransfersDbContext db) : ITransfersComponent
{
    public async Task<Result> Transfer(Guid sourceId, Guid targetId, decimal amount)
    {
        if (sourceId == targetId)
            return Result.Refused("Cannot transfer to the same account.");

        // The Accounts component's tables are never touched: we go through its interface.
        if (await accounts.Get(targetId) is null)
            return Result.NotFound(targetId);

        var withdrawal = await accounts.Withdraw(sourceId, amount);
        if (!withdrawal.Success)
            return withdrawal;

        var deposit = await accounts.Deposit(targetId, amount);
        if (!deposit.Success)
        {
            // No shared transaction between two components that each have their own database:
            // if the 2nd step fails, the 1st one is compensated.
            await accounts.Deposit(sourceId, amount);
            return deposit;
        }

        db.Transfers.Add(new Transfer { SourceId = sourceId, TargetId = targetId, Amount = amount });
        await db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<IReadOnlyList<TransferDto>> History(Guid accountId) =>
        await db.Transfers
            .Where(v => v.SourceId == accountId || v.TargetId == accountId)
            .OrderByDescending(v => v.DateUtc)
            .Select(v => new TransferDto(v.SourceId, v.TargetId, v.Amount, v.DateUtc))
            .ToListAsync();
}
