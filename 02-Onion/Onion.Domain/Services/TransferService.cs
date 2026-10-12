using Onion.Domain.Model;

namespace Onion.Domain.Services;

/// <summary>
/// Layer 2 — Domain Services: a business rule that concerns several entities
/// and naturally belongs to none of them. Pure logic: no data access.
/// </summary>
public class TransferService
{
    public const decimal TransferLimit = 5_000m;

    public void Transfer(Account source, Account target, decimal amount)
    {
        if (source.Id == target.Id)
            throw new BusinessRuleException("Cannot transfer to the same account.");
        if (amount > TransferLimit)
            throw new BusinessRuleException($"A transfer cannot exceed {TransferLimit:0} €.");

        source.Withdraw(amount);
        target.Deposit(amount);
    }
}
