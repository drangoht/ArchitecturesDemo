using Hexa.Adapters.Persistence;
using Hexa.Hexagon.Application;
using Hexa.Hexagon.Domain;

namespace Hexa.Tests.Hexagon;

// The hexagon is tested by plugging test adapters into its ports:
// no web server, no database.
public class UseCasesTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly InMemoryAccounts _accounts = new([new Account(Alice, "Alice", 100m), new Account(Bob, "Bob", 20m)]);
    private readonly SpyNotifications _notifications = new();

    [Fact]
    public async Task A_valid_withdrawal_debits_the_account_and_notifies()
    {
        await new WithdrawMoney(_accounts, _notifications).Execute(Alice, 30m);

        Assert.Equal(70m, (await _accounts.Load(Alice))!.Balance);
        Assert.Equal([$"withdrawal 30 {Alice}"], _notifications.Sent);
    }

    [Fact]
    public async Task A_withdrawal_above_the_balance_is_refused_without_effect()
    {
        var withdrawal = () => new WithdrawMoney(_accounts, _notifications).Execute(Alice, 500m);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(withdrawal);
        Assert.Equal("Insufficient balance.", error.Message);
        Assert.Equal(100m, (await _accounts.Load(Alice))!.Balance);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task An_unknown_account_throws_AccountNotFound()
    {
        var lookup = () => new GetAccount(_accounts).Execute(Guid.NewGuid());

        await Assert.ThrowsAsync<AccountNotFoundException>(lookup);
    }

    [Fact]
    public async Task A_transfer_debits_the_source_and_credits_the_target()
    {
        await new MakeTransfer(_accounts, _notifications).Execute(Alice, Bob, 40m);

        Assert.Equal(60m, (await _accounts.Load(Alice))!.Balance);
        Assert.Equal(60m, (await _accounts.Load(Bob))!.Balance);
        Assert.Single(_notifications.Sent);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_refused()
    {
        var transfer = () => new MakeTransfer(_accounts, _notifications).Execute(Alice, Alice, 10m);

        await Assert.ThrowsAsync<BusinessRuleException>(transfer);
    }
}
