using System.Globalization;
using Ddd.AccountKeeping.Domain;
using Ddd.AccountKeeping.Domain.Accounts;
using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Infrastructure;

/// <summary>
/// "Legacy" external system (simulated), which cannot be changed. Its model is very different from ours:
/// a string of quotes "EURUSD=10832;…", rates multiplied by 10,000, only from the euro,
/// and currencies our bank does not keep.
/// </summary>
public class LegacyQuoteSystem
{
    public virtual string TodaysQuotes() => "EURUSD=10832;EURCHF=9415;EURGBP=8571;EURJPY=1612300";
}

/// <summary>
/// ANTI-CORRUPTION LAYER: it translates the model of the legacy system into our language (Money, Currency).
/// Without it, the "basis points" and the "EURxxx" pairs would end up spreading through the domain.
/// If the provider changes, only this class is rewritten.
/// </summary>
public class ExchangeRateAdapter(LegacyQuoteSystem legacySystem) : ICurrencyConverter
{
    public Task<Money> Convert(Money amount, Currency currency)
    {
        var ratesFromEuro = ReadQuotes(legacySystem.TodaysQuotes());

        // The legacy system only quotes from the euro: any cross rate goes through the euro (USD → CHF…).
        var amountInEuros = amount.Amount / ratesFromEuro[amount.Currency];
        var converted = decimal.Round(amountInEuros * ratesFromEuro[currency], 2, MidpointRounding.ToEven);
        return Task.FromResult(new Money(converted, currency));
    }

    private static Dictionary<Currency, decimal> ReadQuotes(string quotes)
    {
        var rates = new Dictionary<Currency, decimal> { [Currency.Euro] = 1m };
        foreach (var quote in quotes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var (pair, value) = (quote.Split('=')[0], quote.Split('=')[1]);
            if (!pair.StartsWith("EUR"))
                continue;
            try
            {
                rates[Currency.Of(pair[3..])] = decimal.Parse(value, CultureInfo.InvariantCulture) / 10_000m;
            }
            catch (BusinessRuleException)
            {
                // Currency the bank does not keep (GBP, JPY…): it does not get into our model.
            }
        }
        return rates;
    }
}
