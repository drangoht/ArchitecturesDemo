namespace Ddd.AccountKeeping.Domain.ValueObjects;

/// <summary>Strongly typed identifier: impossible to pass a transfer identifier where an account is expected.</summary>
public readonly record struct AccountId(Guid Value)
{
    public static AccountId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed record OwnerName
{
    public string Value { get; }

    private OwnerName(string value) => Value = value;

    public static OwnerName From(string input)
    {
        var cleaned = string.Join(' ', (input ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (cleaned.Length is < 2 or > 70)
            throw new BusinessRuleException("The owner's name must be between 2 and 70 characters long.");
        return new OwnerName(cleaned);
    }

    public override string ToString() => Value;
}
