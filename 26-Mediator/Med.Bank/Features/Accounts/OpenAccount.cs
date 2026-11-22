using Med.Bank.Data;
using Med.Bank.Domain;
using Med.Bank.Pipeline;
using Med.Mediator;

namespace Med.Bank.Features.Accounts;

// A request, its validator and its handler, side by side: the usual layout with a mediator.
public record OpenAccount(string Owner, decimal InitialDeposit) : ICommand<Guid>;

internal class OpenAccountValidator : IValidator<OpenAccount>
{
    public IEnumerable<ValidationError> Validate(OpenAccount request)
    {
        if (string.IsNullOrWhiteSpace(request.Owner))
            yield return new(nameof(request.Owner), "The owner is required.");
        if (request.InitialDeposit < 10)
            yield return new(nameof(request.InitialDeposit), "The initial deposit must be at least 10 €.");
    }
}

internal class OpenAccountHandler(BankDb db) : IRequestHandler<OpenAccount, Guid>
{
    public Task<Guid> Handle(OpenAccount request, CancellationToken ct)
    {
        var account = new Account(Guid.NewGuid(), request.Owner.Trim(), request.InitialDeposit);
        db.Accounts.Add(account);
        db.Operations.Add(new Operation { AccountId = account.Id, Label = "Initial deposit", Amount = request.InitialDeposit });
        return Task.FromResult(account.Id);   // no SaveChanges: the UnitOfWorkBehavior does it
    }
}
