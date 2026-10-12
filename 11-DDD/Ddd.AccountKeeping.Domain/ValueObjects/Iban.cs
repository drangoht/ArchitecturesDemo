namespace Ddd.AccountKeeping.Domain.ValueObjects;

/// <summary>
/// An IBAN cannot exist in an invalid form: the validation (modulo 97 check digits) is done at creation.
/// Elsewhere in the code, receiving an Iban is enough to know it is valid: no need to check a string again.
/// </summary>
public sealed record Iban
{
    /// <summary>Normalized form: without spaces, in upper case.</summary>
    public string Value { get; }

    private Iban(string value) => Value = value;

    public static Iban From(string input)
    {
        var value = new string((input ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        if (value.Length is < 15 or > 34
            || !value.All(char.IsAsciiLetterOrDigit)
            || !char.IsAsciiLetter(value[0]) || !char.IsAsciiLetter(value[1])
            || !char.IsAsciiDigit(value[2]) || !char.IsAsciiDigit(value[3]))
            throw new BusinessRuleException($"Invalid IBAN: \"{input}\".");

        if (Modulo97(value[4..] + value[..4]) != 1)
            throw new BusinessRuleException($"Invalid IBAN (wrong check digits): \"{input}\".");

        return new Iban(value);
    }

    /// <summary>Computes the check digits of a new IBAN from the country code and the BBAN.</summary>
    public static Iban Generate(string countryCode, string bban)
    {
        var checkDigits = 98 - Modulo97(bban + countryCode + "00");
        return From($"{countryCode}{checkDigits:00}{bban}");
    }

    private static int Modulo97(string text)
    {
        var remainder = 0;
        foreach (var character in text)
        {
            var value = char.IsAsciiDigit(character) ? character - '0' : character - 'A' + 10;
            remainder = value < 10 ? (remainder * 10 + value) % 97 : (remainder * 100 + value) % 97;
        }
        return remainder;
    }

    /// <summary>Readable form, in blocks of 4: "FR76 3000 1000 …".</summary>
    public override string ToString() => string.Join(' ', Value.Chunk(4).Select(block => new string(block)));
}
