using System.Globalization;

namespace Ddd.AccountKeeping.Domain.ValueObjects;

/// <summary>
/// Value object: defined by its attributes (no identity), immutable, always valid.
/// Two Money objects with the same amount and the same currency are equal (that is what a record gives).
/// </summary>
public sealed record Currency
{
    public static readonly Currency Euro = new("EUR");
    public static readonly Currency UsDollar = new("USD");
    public static readonly Currency SwissFranc = new("CHF");

    private static readonly Dictionary<string, Currency> Known =
        new[] { Euro, UsDollar, SwissFranc }.ToDictionary(d => d.Code);

    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Currency Of(string code) =>
        Known.TryGetValue(code.Trim().ToUpperInvariant(), out var currency)
            ? currency
            : throw new BusinessRuleException($"Unknown currency: \"{code}\". Currencies kept: {string.Join(", ", Known.Keys)}.");

    public override string ToString() => Code;
}

public sealed record Money
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    public Money(decimal amount, Currency currency)
    {
        if (amount < 0)
            throw new BusinessRuleException("An amount of money cannot be negative.");
        if (decimal.Round(amount, 2) != amount)
            throw new BusinessRuleException("An amount of money has at most two decimals.");
        Amount = amount;
        Currency = currency;
    }

    public static Money Zero(Currency currency) => new(0m, currency);

    public bool IsZero => Amount == 0;

    // The operations refuse to mix currencies: it is a domain rule, carried by the type itself.
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, SameCurrency(a, b));
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, SameCurrency(a, b));
    public static bool operator >(Money a, Money b) => SameCurrency(a, b) is not null && a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => SameCurrency(a, b) is not null && a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => !(a < b);
    public static bool operator <=(Money a, Money b) => !(a > b);

    private static Currency SameCurrency(Money a, Money b) =>
        a.Currency == b.Currency ? a.Currency
            : throw new BusinessRuleException($"Cannot combine {a.Currency} and {b.Currency} without conversion.");

    public override string ToString() => $"{Amount.ToString("N2", CultureInfo.GetCultureInfo("en-GB"))} {Currency}";

    // Technical format (persistence): "1234.56 EUR".
    public string ToText() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency.Code}";

    public static Money FromText(string text)
    {
        var pieces = text.Split(' ');
        return new Money(decimal.Parse(pieces[0], CultureInfo.InvariantCulture), Currency.Of(pieces[1]));
    }
}
