using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Domain.Accounts;

/// <summary>
/// Port to an exchange service. The domain says what it needs, in ITS language (Money, Currency);
/// the infrastructure provides the implementation (here through an anti-corruption layer).
/// </summary>
public interface ICurrencyConverter
{
    Task<Money> Convert(Money amount, Currency currency);
}

/// <summary>
/// Domain service: the transfer involves TWO aggregates and an exchange rate.
/// This rule naturally belongs to neither of the two accounts: it lives here, stateless.
/// </summary>
public class TransferService(ICurrencyConverter converter)
{
    public async Task Transfer(Account source, Account target, Money amount, DateTime date)
    {
        if (source.Id == target.Id)
            throw new BusinessRuleException("Cannot transfer to the same account.");
        if (amount.Currency != source.Currency)
            throw new BusinessRuleException($"A transfer is expressed in the currency of the debited account ({source.Currency}).");

        var creditedAmount = target.Currency == amount.Currency ? amount : await converter.Convert(amount, target.Currency);

        source.Debit(amount, $"Transfer to {target.Owner}", date);
        target.Credit(creditedAmount, $"Transfer from {source.Owner}", date);

        source.Report(new TransferMade(Guid.NewGuid(), source.Id, source.Iban, source.Owner,
            target.Iban, amount, creditedAmount, date));
    }
}
