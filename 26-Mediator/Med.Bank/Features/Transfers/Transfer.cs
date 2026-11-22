using Med.Bank.Data;
using Med.Bank.Domain;
using Med.Bank.Pipeline;
using Med.Mediator;

namespace Med.Bank.Features.Transfers;

public record Transfer(Guid Source, Guid Target, decimal Amount) : ICommand<TransferResult>;

public record TransferResult(decimal SourceBalance, decimal TargetBalance);

internal class TransferValidator : IValidator<Transfer>
{
    public const decimal Limit = 5_000m;

    public IEnumerable<ValidationError> Validate(Transfer request)
    {
        if (request.Amount <= 0)
            yield return new(nameof(request.Amount), "The amount must be strictly positive.");
        if (request.Amount > Limit)
            yield return new(nameof(request.Amount), $"A transfer cannot exceed {Limit:0} €.");
        if (request.Source == request.Target)
            yield return new(nameof(request.Target), "Cannot transfer to the same account.");
    }
}

/// <summary>
/// The transfer moves the money, then ANNOUNCES it. It does not know who listens:
/// the notification to Bob and the compliance review are handlers it never names.
/// </summary>
internal class TransferHandler(BankDb db, IMediator mediator) : IRequestHandler<Transfer, TransferResult>
{
    public async Task<TransferResult> Handle(Transfer request, CancellationToken ct)
    {
        var source = await db.Accounts.FindAsync([request.Source], ct)
            ?? throw new NotFoundException($"Account {request.Source} not found.");
        var target = await db.Accounts.FindAsync([request.Target], ct)
            ?? throw new NotFoundException($"Account {request.Target} not found.");

        source.Withdraw(request.Amount);
        target.Deposit(request.Amount);
        db.Operations.AddRange(
            new Operation { AccountId = source.Id, Label = $"Transfer to {target.Owner}", Amount = -request.Amount },
            new Operation { AccountId = target.Id, Label = $"Transfer from {source.Owner}", Amount = request.Amount });

        await mediator.Publish(new TransferCompleted(source.Id, source.Owner, target.Id, request.Amount), ct);
        return new TransferResult(source.Balance, target.Balance);
    }
}
