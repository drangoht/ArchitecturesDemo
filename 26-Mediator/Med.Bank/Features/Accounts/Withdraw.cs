using Med.Bank.Data;
using Med.Bank.Domain;
using Med.Bank.Pipeline;
using Med.Mediator;

namespace Med.Bank.Features.Accounts;

public record Withdraw(Guid AccountId, decimal Amount) : ICommand<decimal>;

internal class WithdrawValidator : IValidator<Withdraw>
{
    public IEnumerable<ValidationError> Validate(Withdraw request)
    {
        if (request.Amount <= 0)
            yield return new(nameof(request.Amount), "The amount must be strictly positive.");
    }
}

/// <summary>Only the business: no validation, no logging, no SaveChanges. The behaviors take care of them.</summary>
internal class WithdrawHandler(BankDb db) : IRequestHandler<Withdraw, decimal>
{
    public async Task<decimal> Handle(Withdraw request, CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([request.AccountId], ct)
            ?? throw new NotFoundException($"Account {request.AccountId} not found.");

        account.Withdraw(request.Amount);
        db.Operations.Add(new Operation { AccountId = account.Id, Label = "Withdrawal", Amount = -request.Amount });
        return account.Balance;
    }
}
