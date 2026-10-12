using Ui.Bank;

namespace Ui.Tests;

/// <summary>The model alone: the rules that both interfaces display, but that only the bank enforces.</summary>
public class BankTests
{
    private readonly InMemoryBank _bank = new(new FixedClock());

    [Fact]
    public async Task A_transfer_debits_the_source_and_credits_the_target()
    {
        var transfer = await _bank.TransferAsync(InMemoryBank.Alice, InMemoryBank.Bob, 250.50m);

        Assert.Equal((749.50m, 750.50m), (transfer.SourceBalance, transfer.TargetBalance));
        var operations = await _bank.OperationsAsync(InMemoryBank.Bob);
        Assert.Equal("Transfer from Alice (Current account)", operations[0].Label);   // the most recent first
        Assert.Equal("11/10/2026 14:30", Format.Date(operations[0].Date));          // in Paris time
    }

    [Theory]
    [InlineData("0", "The amount must be positive.")]
    [InlineData("-5", "The amount must be positive.")]
    [InlineData("12.345", "The amount is given to the cent.")]
    [InlineData("1000.01", "Insufficient balance: 1,000.00 € available.")]
    public async Task The_bank_refuses_what_its_rules_forbid(string text, string message)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var refusal = await Assert.ThrowsAsync<BusinessError>(() => _bank.TransferAsync(InMemoryBank.Alice, InMemoryBank.Bob, amount));

        Assert.Equal(message, refusal.Message);
        Assert.Equal(1_000m, (await _bank.AccountAsync(InMemoryBank.Alice)).Balance);
    }

    [Fact]
    public async Task An_account_transfers_nothing_to_itself_and_an_unknown_account_is_refused()
    {
        var sameAccount = await Assert.ThrowsAsync<BusinessError>(() => _bank.TransferAsync(InMemoryBank.Alice, InMemoryBank.Alice, 10m));
        await Assert.ThrowsAsync<BusinessError>(() => _bank.TransferAsync(InMemoryBank.Alice, Guid.NewGuid(), 10m));

        Assert.Equal("Choose two different accounts.", sameAccount.Message);
        Assert.Equal(1_000m, (await _bank.AccountAsync(InMemoryBank.Alice)).Balance);
    }
}
