using Ddd.Compliance;
using Ddd.AccountKeeping.Domain;
using Ddd.AccountKeeping.Domain.Accounts;
using Ddd.AccountKeeping.Domain.ValueObjects;
using Ddd.AccountKeeping.Infrastructure;

namespace Ddd.Tests;

public class DomainTests
{
    private static readonly DateTime Monday = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Money Euros(decimal amount) => new(amount, Currency.Euro);

    private static async Task<Account> NewAccount(string owner, Money deposit)
    {
        var factory = new AccountFactory(new InMemoryAccounts());
        var account = await factory.Open(OwnerName.From(owner), deposit, Monday);
        account.ExtractEvents();
        return account;
    }

    [Fact]
    public async Task The_withdrawal_limit_applies_to_the_day()
    {
        var account = await NewAccount("Alice", Euros(5_000m));
        account.Withdraw(Euros(600m), Monday);

        var error = Assert.Throws<BusinessRuleException>(() => account.Withdraw(Euros(600m), Monday.AddHours(3)));
        account.Withdraw(Euros(600m), Monday.AddDays(1));  // the next day, it is allowed

        Assert.Contains("Daily withdrawal limit", error.Message);
        Assert.Equal(Euros(3_800m), account.Balance);
    }

    [Fact]
    public async Task A_blocked_account_can_no_longer_be_debited()
    {
        var account = await NewAccount("Alice", Euros(500m));
        account.Block("Check", Monday);

        Assert.Throws<BusinessRuleException>(() => account.Withdraw(Euros(10m), Monday));
        Assert.IsType<AccountBlocked>(Assert.Single(account.DomainEvents));
    }

    [Fact]
    public async Task A_transfer_between_currencies_goes_through_the_anti_corruption_layer()
    {
        var alice = await NewAccount("Alice", Euros(500m));
        var carla = await NewAccount("Carla", new Money(0m + 100m, Currency.UsDollar));
        var service = new TransferService(new ExchangeRateAdapter(new LegacyQuoteSystem()));

        await service.Transfer(alice, carla, Euros(100m), Monday);

        Assert.Equal(Euros(400m), alice.Balance);
        Assert.Equal(new Money(208.32m, Currency.UsDollar), carla.Balance);  // 100 € × 1.0832
        var transfer = Assert.IsType<TransferMade>(Assert.Single(alice.DomainEvents));
        Assert.Equal(new Money(108.32m, Currency.UsDollar), transfer.CreditedAmount);
    }

    [Fact]
    public async Task The_anti_corruption_layer_computes_cross_rates_and_ignores_unknown_currencies()
    {
        var adapter = new ExchangeRateAdapter(new LegacyQuoteSystem());

        var francs = await adapter.Convert(new Money(108.32m, Currency.UsDollar), Currency.SwissFranc);

        Assert.Equal(new Money(94.15m, Currency.SwissFranc), francs);  // USD → EUR → CHF; GBP and JPY ignored
    }

    [Fact]
    public void Compliance_reports_a_large_transfer_then_structuring()
    {
        var file = new VigilanceFile(Guid.NewGuid(), "Bob");

        var large = file.Observe(new ObservedTransfer(Monday, 12_000m, "FR76…"));
        file.Observe(new ObservedTransfer(Monday.AddMinutes(2), 50m, "FR76…"));
        var third = file.Observe(new ObservedTransfer(Monday.AddMinutes(4), 50m, "FR76…"));

        Assert.Equal(AlertLevel.High, Assert.Single(large).Level);
        Assert.Equal(AlertLevel.Critical, Assert.Single(third).Level);
        Assert.True(file.BlockRequested);
    }

    private class InMemoryAccounts : IAccounts
    {
        public Task<Account?> ById(AccountId id) => Task.FromResult<Account?>(null);
        public Task<Account?> ByIban(Iban iban) => Task.FromResult<Account?>(null);
        public Task<IReadOnlyList<Account>> All() => Task.FromResult<IReadOnlyList<Account>>([]);
        public Task Add(Account account) => Task.CompletedTask;
        public Task<long> NextAccountNumber() => Task.FromResult(42L);
    }
}
