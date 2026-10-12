using Cqrs.Write.Domain;
using Cqrs.Write.Persistence;

namespace Cqrs.Write.Commands;

public record WithdrawMoney(Guid AccountId, decimal Amount) : ICommand;

public class WithdrawMoneyValidator : IValidator<WithdrawMoney>
{
    public IEnumerable<string> Validate(WithdrawMoney command)
    {
        if (command.Amount <= 0)
            yield return "The amount must be strictly positive.";
    }
}

public class WithdrawMoneyHandler(WriteDb db, TimeProvider clock) : ICommandHandler<WithdrawMoney>
{
    public async Task Handle(WithdrawMoney command)
    {
        var account = await db.Accounts.FindAsync(command.AccountId) ?? throw new AccountNotFoundException(command.AccountId);
        account.Withdraw(command.Amount, clock.GetUtcNow().UtcDateTime);
        await db.Save();
    }
}
