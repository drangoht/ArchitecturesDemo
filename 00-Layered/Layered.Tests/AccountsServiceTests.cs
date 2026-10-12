using Layered.Data;
using Layered.Business;

namespace Layered.Tests;

/// <summary>
/// Testing the business layer forces us to handle the data layer: even the fake DAO
/// returns AccountEntity objects. The business test is coupled to the persistence schema.
/// </summary>
public class AccountsServiceTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private readonly FakeDao _dao = new(
        new AccountEntity { Id = Alice, Owner = "Alice", Balance = 100m },
        new AccountEntity { Id = Bob, Owner = "Bob", Balance = 0m });

    private AccountsService Service => new(_dao);

    [Fact]
    public async Task A_valid_withdrawal_debits_and_saves()
    {
        var account = await Service.Withdraw(Alice, 30m);

        Assert.Equal(70m, account.Balance);
        Assert.Equal(1, _dao.SaveCount);
    }

    [Fact]
    public async Task A_withdrawal_that_is_too_large_is_refused_without_saving()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.Withdraw(Alice, 500m));

        Assert.Equal(0, _dao.SaveCount);
    }

    [Fact]
    public async Task A_transfer_moves_the_money()
    {
        await Service.Transfer(Alice, Bob, 40m);

        Assert.Equal(60m, (await _dao.ById(Alice))!.Balance);
        Assert.Equal(40m, (await _dao.ById(Bob))!.Balance);
    }

    [Fact]
    public async Task A_transfer_above_the_limit_is_refused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Service.Transfer(Alice, Bob, 6_000m));
    }

    [Fact]
    public async Task An_unknown_account_throws_AccountNotFound()
    {
        await Assert.ThrowsAsync<AccountNotFoundException>(() => Service.Get(Guid.NewGuid()));
    }

    private class FakeDao(params AccountEntity[] accounts) : IAccountsDao
    {
        private readonly List<AccountEntity> _accounts = [.. accounts];
        public int SaveCount { get; private set; }

        public Task<AccountEntity?> ById(Guid id) => Task.FromResult(_accounts.FirstOrDefault(c => c.Id == id));
        public Task Add(AccountEntity account) { _accounts.Add(account); return Task.CompletedTask; }
        public Task<bool> IsEmpty() => Task.FromResult(_accounts.Count == 0);
        public Task Save() { SaveCount++; return Task.CompletedTask; }
    }
}
