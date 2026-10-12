using Functional.Core;

namespace Functional.Tests;

// Testing a pure function: values as input, an expected value as output. Nothing else.
public class RulesTests
{
    private static readonly Account Alice = new(Guid.NewGuid(), "Alice", 1000m);
    private static readonly Account Bob = new(Guid.NewGuid(), "Bob", 0m);

    [Theory]
    [InlineData(1000, 30, 970)]
    [InlineData(1000, 1000, 0)]
    public void A_possible_withdrawal_is_accepted(int balance, int amount, int expectedBalance)
    {
        var decision = Rules.Withdraw(Alice with { Balance = balance }, amount);

        var accepted = Assert.IsType<WithdrawalDecision.Accepted>(decision);
        Assert.Equal(expectedBalance, accepted.UpdatedAccount.Balance);
    }

    [Theory]
    [InlineData(0, "The amount must be strictly positive.")]
    [InlineData(-5, "The amount must be strictly positive.")]
    [InlineData(1001, "Insufficient balance.")]
    public void An_impossible_withdrawal_is_refused(int amount, string reason)
    {
        var decision = Rules.Withdraw(Alice, amount);

        Assert.Equal(new WithdrawalDecision.Refused(reason), decision);
    }

    [Fact]
    public void A_decision_never_modifies_the_original_account()
    {
        Rules.Withdraw(Alice, 100m);

        Assert.Equal(1000m, Alice.Balance);
    }

    [Fact]
    public void A_transfer_below_the_limit_moves_the_money()
    {
        var decision = Rules.Transfer(Alice, Bob, 300m, alreadyTransferredToday: 0m);

        Assert.Equal(new TransferDecision.Accepted(Alice with { Balance = 700m }, Bob with { Balance = 300m }, 300m), decision);
    }

    [Fact]
    public void The_daily_limit_takes_into_account_what_was_already_transferred()
    {
        // "Time" is a mere piece of data: no need to fake a clock.
        var decision = Rules.Transfer(Alice with { Balance = 10_000m }, Bob, 600m, alreadyTransferredToday: 1_500m);

        Assert.IsType<TransferDecision.Refused>(decision);
    }

    [Fact]
    public void A_transfer_reuses_the_refusals_of_the_withdrawal_rule()
    {
        var decision = Rules.Transfer(Alice, Bob, 1_500m, alreadyTransferredToday: 0m);

        Assert.Equal(new TransferDecision.Refused("Insufficient balance."), decision);
    }
}
