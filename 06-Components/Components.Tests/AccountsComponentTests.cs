using Components.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace Components.Tests;

// The component is tested as a black box, through its public interface, with its real (in-memory) database.
public class AccountsComponentTests
{
    private readonly IAccountsComponent _accounts = new ServiceCollection()
        .AddAccountsComponent(databaseName: Guid.NewGuid().ToString())
        .BuildServiceProvider()
        .GetRequiredService<IAccountsComponent>();

    [Fact]
    public async Task An_opened_account_can_be_read()
    {
        var opened = await _accounts.Open("Alice", 100m);

        Assert.Equal(opened, await _accounts.Get(opened.Id));
    }

    [Fact]
    public async Task A_withdrawal_that_is_too_large_is_refused_and_nothing_changes()
    {
        var alice = await _accounts.Open("Alice", 100m);

        var result = await _accounts.Withdraw(alice.Id, 500m);

        Assert.Equal(Result.Refused("Insufficient balance."), result);
        Assert.Equal(100m, (await _accounts.Get(alice.Id))!.Balance);
    }

    [Fact]
    public async Task An_unknown_account_is_reported()
    {
        var result = await _accounts.Withdraw(Guid.NewGuid(), 10m);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public void The_internal_entity_stays_testable_thanks_to_InternalsVisibleTo()
    {
        var account = new Account(Guid.NewGuid(), "Alice", 50m);

        Assert.Equal(ResultStatus.Refused, account.Withdraw(-1m).Status);
    }
}
