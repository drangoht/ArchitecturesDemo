using Cqrs.Write.Domain;
using Cqrs.Write.Persistence;
using Cqrs.Events;

namespace Cqrs.Write.Commands;

public record MakeTransfer(Guid TransferId, Guid SourceId, Guid TargetId, decimal Amount) : ICommand;

public class MakeTransferValidator : IValidator<MakeTransfer>
{
    public const decimal Limit = 5_000m;

    public IEnumerable<string> Validate(MakeTransfer command)
    {
        if (command.Amount <= 0)
            yield return "The amount must be strictly positive.";
        if (command.Amount > Limit)
            yield return $"A transfer cannot exceed {Limit:0} €.";
        if (command.SourceId == command.TargetId)
            yield return "Cannot transfer to the same account.";
    }
}

public class MakeTransferHandler(WriteDb db, TimeProvider clock) : ICommandHandler<MakeTransfer>
{
    public async Task Handle(MakeTransfer command)
    {
        var source = await db.Accounts.FindAsync(command.SourceId) ?? throw new AccountNotFoundException(command.SourceId);
        var target = await db.Accounts.FindAsync(command.TargetId) ?? throw new AccountNotFoundException(command.TargetId);

        source.Debit(command.Amount);
        target.Credit(command.Amount);

        // Both accounts and the event are saved together.
        await db.Save(new TransferMade(command.TransferId, source.Id, target.Id, command.Amount,
            source.Balance, target.Balance, clock.GetUtcNow().UtcDateTime));
    }
}
