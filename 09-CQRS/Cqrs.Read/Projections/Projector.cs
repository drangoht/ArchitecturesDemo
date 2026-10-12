using Cqrs.Events;
using Cqrs.Read.Model;

namespace Cqrs.Read.Projections;

/// <summary>
/// Turns the domain events into read models. It is the only code that writes to the read database.
/// The whole read database could be thrown away and rebuilt by replaying the events from the start.
/// </summary>
public class Projector(ReadDb db)
{
    public async Task<long> LastSequence() => (await GetCheckpoint()).LastSequence;

    public async Task Apply(RecordedEvent recorded)
    {
        var checkpoint = await GetCheckpoint();
        if (recorded.Sequence <= checkpoint.LastSequence)
            return;  // already projected: replaying an event has no effect

        var stats = await db.Statistics.FindAsync(1) ?? db.Statistics.Add(new Statistics()).Entity;

        switch (recorded.Event)
        {
            case AccountOpened e:
                db.Statements.Add(new AccountStatement
                {
                    Id = e.AccountId, Owner = e.Owner, Balance = e.InitialDeposit,
                    OperationCount = 1, LastOperationAt = e.Date
                });
                AddLine(e.AccountId, "Account opening", e.InitialDeposit, e.InitialDeposit, e.Date);
                stats.AccountCount++;
                stats.TotalOutstanding += e.InitialDeposit;
                break;

            case MoneyWithdrawn e:
                await Update(e.AccountId, e.NewBalance, e.Date);
                AddLine(e.AccountId, "Withdrawal", -e.Amount, e.NewBalance, e.Date);
                stats.TotalOutstanding -= e.Amount;
                break;

            case TransferMade e:
                var source = await Update(e.SourceId, e.SourceBalance, e.Date);
                var target = await Update(e.TargetId, e.TargetBalance, e.Date);
                // The label is written once and for all, with the other owner's name.
                AddLine(e.SourceId, $"Transfer to {target.Owner}", -e.Amount, e.SourceBalance, e.Date);
                AddLine(e.TargetId, $"Transfer received from {source.Owner}", e.Amount, e.TargetBalance, e.Date);
                stats.TransferCount++;
                stats.TotalTransferredAmount += e.Amount;
                break;
        }

        checkpoint.LastSequence = recorded.Sequence;
        await db.SaveChangesAsync();
    }

    private async Task<AccountStatement> Update(Guid accountId, decimal balance, DateTime date)
    {
        var statement = (await db.Statements.FindAsync(accountId))!;
        statement.Balance = balance;
        statement.OperationCount++;
        statement.LastOperationAt = date;
        return statement;
    }

    private void AddLine(Guid accountId, string label, decimal amount, decimal balanceAfter, DateTime date) =>
        db.Operations.Add(new OperationLine
        {
            AccountId = accountId, Label = label, Amount = amount, BalanceAfter = balanceAfter, Date = date
        });

    private async Task<Checkpoint> GetCheckpoint() =>
        await db.Checkpoints.FindAsync(1) ?? db.Checkpoints.Add(new Checkpoint()).Entity;
}
