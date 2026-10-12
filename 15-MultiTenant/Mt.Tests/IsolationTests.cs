using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Mt.Bank;
using Mt.Tenants;

namespace Mt.Tests;

/// <summary>The data safety net: global filter, stamping, refusal of cross writes, choice of the database.</summary>
public class IsolationTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly BankFactory _factory = new(new InMemoryDatabaseRoot());

    private static Tenant New(string id, Plan plan = Plan.Standard, TenantRules? rules = null) =>
        Plans.Create(new TenantDefinition(id, $"Bank {id}", plan, rules ?? new TenantRules(1_000m, 0m, 0m, 10)));

    private BankingService Bank(Tenant tenant) =>
        new(_factory.Open(tenant), TenantContext.For(tenant), TimeProvider.System);

    private async Task Seed(Tenant tenant, params Account[] accounts)
    {
        await using var db = _factory.Open(tenant);
        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_same_identifier_designates_a_different_account_at_each_tenant()
    {
        var alpha = New("alpha");
        var beta = New("beta");
        await Seed(alpha, new Account(Alice, "Alice", 1_000m));
        await Seed(beta, new Account(Alice, "Alice", 3_000m));  // same Guid, same shared database: the key includes the tenant

        Assert.Equal(1_000m, (await Bank(alpha).Get(Alice))!.Balance);
        Assert.Equal(3_000m, (await Bank(beta).Get(Alice))!.Balance);
    }

    [Fact]
    public async Task A_tenant_neither_sees_nor_touches_another_s_accounts()
    {
        var alpha = New("alpha");
        var beta = New("beta");
        var account = await Bank(alpha).Open("Zoe", 100m);

        Assert.Null(await Bank(beta).Get(account.Id));
        Assert.DoesNotContain(await Bank(beta).List(), c => c.Id == account.Id);
        await Assert.ThrowsAsync<AccountNotFoundException>(() => Bank(beta).Withdraw(account.Id, 10m));
        Assert.Equal(100m, (await Bank(alpha).Get(account.Id))!.Balance);
    }

    [Fact]
    public async Task Without_a_tenant_a_query_fails_instead_of_returning_everything()
    {
        await Seed(New("alpha"), new Account(Alice, "Alice", 1_000m));
        await using var db = _factory.OpenWithoutTenant(BankFactory.SharedDatabase);

        await Assert.ThrowsAsync<NoTenantException>(() => db.Accounts.ToListAsync());
        // Seeing all the tenants remains possible, but only explicitly.
        Assert.Single(await db.Accounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task An_insertion_is_stamped_with_the_current_tenant()
    {
        var account = await Bank(New("alpha")).Open("Zoe", 100m);

        await using var db = _factory.OpenWithoutTenant(BankFactory.SharedDatabase);
        Assert.Equal("alpha", (await db.Accounts.IgnoreQueryFilters().SingleAsync(c => c.Id == account.Id)).TenantId);
        Assert.All(await db.Operations.IgnoreQueryFilters().ToListAsync(), o => Assert.Equal("alpha", o.TenantId));
    }

    [Fact]
    public async Task Saving_data_of_another_tenant_is_refused()
    {
        var alpha = New("alpha");
        await Seed(alpha, new Account(Alice, "Alice", 1_000m));
        Account alphaAccount;
        await using (var db = _factory.Open(alpha))
            alphaAccount = await db.Accounts.AsNoTracking().SingleAsync();

        // A bug passes an entity of alpha to beta's DbContext (shared cache, static variable…).
        await using var dbBeta = _factory.Open(New("beta"));
        alphaAccount.Debit(1_000m);
        dbBeta.Update(alphaAccount);

        await Assert.ThrowsAsync<CrossTenantLeakException>(() => dbBeta.SaveChangesAsync());
    }

    [Fact]
    public async Task The_data_of_a_premium_tenant_is_in_its_own_database()
    {
        var gamma = New("gamma", Plan.Premium);
        await Bank(gamma).Open("Zoe", 100m);
        await Bank(New("alpha")).Open("Yann", 50m);

        Assert.Equal(["bank-gamma", "bank-shared"], _factory.Databases);
        await using var shared = _factory.OpenWithoutTenant(BankFactory.SharedDatabase);
        Assert.DoesNotContain(await shared.Accounts.IgnoreQueryFilters().ToListAsync(), c => c.TenantId == "gamma");
    }

    [Fact]
    public void All_the_data_of_a_tenant_is_filtered_and_carries_the_tenant_in_its_key()
    {
        // Architecture test: an entity added tomorrow will be protected, or this test will fail.
        var tenantTypes = typeof(Account).Assembly.GetTypes()
            .Where(t => t.IsClass && typeof(IBelongsToTenant).IsAssignableFrom(t)).ToList();
        using var db = _factory.OpenWithoutTenant("model");

        Assert.NotEmpty(tenantTypes);
        foreach (var type in tenantTypes)
        {
            var entity = db.Model.FindEntityType(type);
            Assert.True(entity is not null, $"{type.Name} is not in the EF model.");
            Assert.NotEmpty(entity.GetDeclaredQueryFilters());
            Assert.Contains(entity.FindPrimaryKey()!.Properties, p => p.Name == nameof(IBelongsToTenant.TenantId));
        }
    }

    [Fact]
    public void The_context_does_not_change_tenant_along_the_way()
    {
        var context = TenantContext.For(New("alpha"));

        Assert.Throws<InvalidOperationException>(() => context.Set(New("beta")));
        Assert.Throws<NoTenantException>(() => new TenantContext().Require());
    }

    [Fact]
    public async Task The_business_rules_are_those_of_the_tenant()
    {
        var bob = Guid.NewGuid();
        var cautious = New("cautious", rules: new TenantRules(TransferLimit: 500m, TransferFee: 0m, MonthlyFee: 0m, MaxAccounts: 10));
        var paying = New("paying", rules: new TenantRules(TransferLimit: 5_000m, TransferFee: 0.5m, MonthlyFee: 0m, MaxAccounts: 10));
        await Seed(cautious, new Account(Alice, "Alice", 1_000m), new Account(bob, "Bob", 0m));
        await Seed(paying, new Account(Alice, "Alice", 1_000m), new Account(bob, "Bob", 0m));

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => Bank(cautious).Transfer(Alice, bob, 800m));
        var transfer = await Bank(paying).Transfer(Alice, bob, 800m);

        Assert.Contains("Transfer limit", refusal.Message);
        Assert.Equal(0.5m, transfer.Fee);
        Assert.Equal(199.5m, transfer.SourceBalance);
    }

    [Fact]
    public async Task The_account_quota_is_the_tenant_s()
    {
        var small = New("small", rules: new TenantRules(1_000m, 0m, 0m, MaxAccounts: 2));
        await Bank(small).Open("One", 0m);
        await Bank(small).Open("Two", 0m);
        await Bank(New("neighbour")).Open("Three", 0m);  // the accounts of another bank do not count

        var refusal = await Assert.ThrowsAsync<BusinessRuleException>(() => Bank(small).Open("Three", 0m));
        Assert.Contains("quota of 2 accounts", refusal.Message);
    }

    [Fact]
    public async Task The_monthly_fees_are_charged_only_once_a_month()
    {
        var alpha = New("alpha", rules: new TenantRules(1_000m, 0m, MonthlyFee: 2m, MaxAccounts: 10));
        await Seed(alpha, new Account(Alice, "Alice", 1_000m));

        Assert.Equal(1, await Bank(alpha).ChargeMonthlyFees("2026-10"));
        Assert.Equal(0, await Bank(alpha).ChargeMonthlyFees("2026-10"));
        Assert.Equal(1, await Bank(alpha).ChargeMonthlyFees("2026-11"));
        Assert.Equal(996m, (await Bank(alpha).Get(Alice))!.Balance);
    }
}
