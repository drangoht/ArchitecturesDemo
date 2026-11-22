using Res.Resilience;

namespace Res.Bank;

public record TransferOutcome(string Status, decimal SourceBalance, decimal TargetBalance);

public class FraudCheckUnavailableException(string reason, TimeSpan retryAfter)
    : Exception($"The fraud check is unavailable ({reason}): try again later.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// A transfer asks the partner for a fraud check. When the partner cannot answer, whatever the reason,
/// the FALLBACK decides: a small transfer goes through, marked unverified, to be reviewed later;
/// a large one is refused for now (503). It is a business decision, not a technical one.
/// </summary>
public class TransferService(Ledger ledger, IFraudCheck fraud)
{
    public const decimal Limit = 5_000m;
    public const decimal UnverifiedLimit = 500m;

    public async Task<TransferOutcome> Transfer(Guid source, Guid target, decimal amount, CancellationToken ct)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be strictly positive.");
        if (amount > Limit)
            throw new BusinessRuleException($"A transfer cannot exceed {Limit:0} €.");
        if (source == target)
            throw new BusinessRuleException("Cannot transfer to the same account.");
        ledger.CheckTransferPossible(source, target, amount);   // before calling the partner: no call wasted

        bool approved;
        try
        {
            approved = await fraud.IsApproved(source, amount, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutRejectedException
                                      or BrokenCircuitException or BulkheadRejectedException)
        {
            if (amount > UnverifiedLimit)
                throw new FraudCheckUnavailableException(e.GetType().Name,
                    e is BrokenCircuitException broken ? broken.RetryAfter : TimeSpan.FromSeconds(5));

            var (from, to) = ledger.Move(source, target, amount, " (unverified)");
            return new TransferOutcome("accepted-unverified", from, to);
        }

        if (!approved)
            throw new BusinessRuleException("Refused by the fraud check.");
        var (sourceBalance, targetBalance) = ledger.Move(source, target, amount, "");
        return new TransferOutcome("accepted", sourceBalance, targetBalance);
    }
}
