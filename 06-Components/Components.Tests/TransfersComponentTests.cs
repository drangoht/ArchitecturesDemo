using Components.Accounts;
using Components.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace Components.Tests;

public class TransfersComponentTests
{
    [Fact]
    public async Task A_transfer_goes_through_the_Accounts_component_and_is_recorded()
    {
        var services = new ServiceCollection()
            .AddAccountsComponent(Guid.NewGuid().ToString())
            .AddTransfersComponent(Guid.NewGuid().ToString())
            .BuildServiceProvider();
        var accounts = services.GetRequiredService<IAccountsComponent>();
        var transfers = services.GetRequiredService<ITransfersComponent>();
        var alice = await accounts.Open("Alice", 100m);
        var bob = await accounts.Open("Bob", 0m);

        var result = await transfers.Transfer(alice.Id, bob.Id, 40m);

        Assert.True(result.Success);
        Assert.Equal(60m, (await accounts.Get(alice.Id))!.Balance);
        Assert.Equal(40m, (await accounts.Get(bob.Id))!.Balance);
        Assert.Single(await transfers.History(bob.Id));
    }

    [Fact]
    public async Task If_the_deposit_fails_the_withdrawal_is_compensated()
    {
        // The components only know each other through interfaces: Accounts can be replaced by a fake.
        var fake = new FakeAccounts();
        var transfers = new ServiceCollection()
            .AddSingleton<IAccountsComponent>(fake)
            .AddTransfersComponent(Guid.NewGuid().ToString())
            .BuildServiceProvider()
            .GetRequiredService<ITransfersComponent>();

        var result = await transfers.Transfer(FakeAccounts.Source, FakeAccounts.Target, 40m);

        Assert.False(result.Success);
        Assert.Equal(["withdrawal source 40", "deposit target 40 (refused)", "deposit source 40"], fake.Log);
        Assert.Empty(await transfers.History(FakeAccounts.Source));
    }

    private class FakeAccounts : IAccountsComponent
    {
        public static readonly Guid Source = Guid.NewGuid();
        public static readonly Guid Target = Guid.NewGuid();

        public List<string> Log { get; } = [];

        public Task<AccountDto?> Get(Guid accountId) =>
            Task.FromResult<AccountDto?>(new AccountDto(accountId, "?", 100m));

        public Task<Result> Withdraw(Guid accountId, decimal amount)
        {
            Log.Add($"withdrawal {Name(accountId)} {amount}");
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> Deposit(Guid accountId, decimal amount)
        {
            if (accountId == Target)
            {
                Log.Add($"deposit target {amount} (refused)");
                return Task.FromResult(Result.Refused("Target account blocked."));
            }
            Log.Add($"deposit {Name(accountId)} {amount}");
            return Task.FromResult(Result.Ok());
        }

        public Task<AccountDto> Open(string owner, decimal initialDeposit, Guid? id = null) =>
            throw new NotSupportedException();

        private static string Name(Guid id) => id == Source ? "source" : "target";
    }
}
