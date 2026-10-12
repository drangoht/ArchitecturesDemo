using Ddd.AccountKeeping.Domain;
using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.Tests;

public class ValueObjectsTests
{
    private static Money Euros(decimal amount) => new(amount, Currency.Euro);

    [Fact]
    public void Two_identical_value_objects_are_equal()
    {
        Assert.Equal(Euros(10m), Euros(10.00m));
        Assert.NotEqual(Euros(10m), new Money(10m, Currency.UsDollar));
    }

    [Fact]
    public void Euros_and_dollars_are_not_added_together()
    {
        Assert.Throws<BusinessRuleException>(() => Euros(10m) + new Money(10m, Currency.UsDollar));
        Assert.Throws<BusinessRuleException>(() => Euros(10m) > new Money(1m, Currency.UsDollar));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.234")]
    public void An_invalid_amount_cannot_exist(string amount)
    {
        Assert.Throws<BusinessRuleException>(() => Euros(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_valid_iban_is_normalized_and_displayed_in_blocks()
    {
        var iban = Iban.From("fr76 3000 6000 0112 3456 7890 189");

        Assert.Equal("FR7630006000011234567890189", iban.Value);
        Assert.Equal("FR76 3000 6000 0112 3456 7890 189", iban.ToString());
    }

    [Theory]
    [InlineData("FR77 3000 6000 0112 3456 7890 189")]  // wrong check digits
    [InlineData("not an iban")]
    public void An_invalid_iban_cannot_exist(string input)
    {
        Assert.Throws<BusinessRuleException>(() => Iban.From(input));
    }

    [Fact]
    public void A_generated_iban_has_valid_check_digits()
    {
        var iban = Iban.Generate("FR", "3000100001000000000420" + "0");

        Assert.Equal(iban, Iban.From(iban.Value));
    }

    [Fact]
    public void The_owner_name_is_cleaned()
    {
        Assert.Equal("Alice Martin", OwnerName.From("  Alice   Martin ").Value);
    }
}
