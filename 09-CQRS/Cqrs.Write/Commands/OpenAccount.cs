using Cqrs.Write.Domain;
using Cqrs.Write.Persistence;

namespace Cqrs.Write.Commands;

/// <summary>The identifier is provided by the caller: that way the command has nothing to return.</summary>
public record OpenAccount(Guid AccountId, string Owner, decimal InitialDeposit) : ICommand;

public class OpenAccountValidator : IValidator<OpenAccount>
{
    public IEnumerable<string> Validate(OpenAccount command)
    {
        if (string.IsNullOrWhiteSpace(command.Owner))
            yield return "The owner is required.";
        if (command.InitialDeposit < 10)
            yield return "The initial deposit must be at least 10 €.";
    }
}

public class OpenAccountHandler(WriteDb db, TimeProvider clock) : ICommandHandler<OpenAccount>
{
    public async Task Handle(OpenAccount command)
    {
        if (await db.Accounts.FindAsync(command.AccountId) is not null)
            throw new BusinessRuleException("This account already exists.");

        db.Accounts.Add(Account.Open(command.AccountId, command.Owner.Trim(), command.InitialDeposit,
            clock.GetUtcNow().UtcDateTime));
        await db.Save();
    }
}
