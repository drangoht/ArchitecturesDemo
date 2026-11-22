using Med.Bank.Data;
using Med.Bank.Domain;
using Med.Bank.Features.Accounts;
using Med.Bank.Features.Transfers;
using Med.Bank.Pipeline;
using Med.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Tests;

/// <summary>
/// The bank through the mediator, without HTTP. Each Send is one scope: what is read back
/// in the next Send was really saved.
/// </summary>
public class BankTests
{
    [Fact]
    public async Task A_withdrawal_is_saved_although_its_handler_never_calls_SaveChanges()
    {
        using var bank = new TestBank();

        Assert.Equal(900m, await bank.Send(new Withdraw(BankDb.Alice, 100m)));

        var alice = await bank.Send(new GetAccount(BankDb.Alice));
        Assert.Equal(900m, alice.Balance);
        Assert.Equal(new OperationView("Withdrawal", -100m), alice.LatestOperations.Single());
    }

    [Fact]
    public async Task A_query_never_saves_what_is_pending_in_the_unit_of_work()
    {
        using var bank = new TestBank();

        using (var scope = bank.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BankDb>();
            (await db.Accounts.FindAsync(BankDb.Alice))!.Withdraw(1m);   // pending change, never saved
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GetAccount(BankDb.Alice));
        }

        Assert.Equal(1000m, (await bank.Send(new GetAccount(BankDb.Alice))).Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task An_invalid_withdrawal_stops_in_the_pipeline_before_its_handler(int amount)
    {
        using var bank = new TestBank();

        var error = await Assert.ThrowsAsync<RequestValidationException>(() => bank.Send(new Withdraw(BankDb.Alice, amount)));

        Assert.Equal("Amount", error.Errors.Single().Field);
        Assert.Empty((await bank.Send(new GetAccount(BankDb.Alice))).LatestOperations);
    }

    [Fact]
    public async Task A_business_rule_broken_in_the_handler_saves_nothing()
    {
        using var bank = new TestBank();

        await Assert.ThrowsAsync<BusinessRuleException>(() => bank.Send(new Withdraw(BankDb.Alice, 1_001m)));

        Assert.Equal(1000m, (await bank.Send(new GetAccount(BankDb.Alice))).Balance);
    }

    [Fact]
    public async Task An_unknown_account_throws_NotFound()
    {
        using var bank = new TestBank();

        await Assert.ThrowsAsync<NotFoundException>(() => bank.Send(new GetAccount(Guid.NewGuid())));
        await Assert.ThrowsAsync<NotFoundException>(() => bank.Send(new Transfer(BankDb.Alice, Guid.NewGuid(), 10m)));
    }

    [Fact]
    public async Task Opening_an_account_saves_it_with_its_initial_deposit()
    {
        using var bank = new TestBank();

        var id = await bank.Send(new OpenAccount(" Chloe ", 50m));

        var chloe = await bank.Send(new GetAccount(id));
        Assert.Equal(("Chloe", 50m), (chloe.Owner, chloe.Balance));
        Assert.Equal(new OperationView("Initial deposit", 50m), chloe.LatestOperations.Single());
    }

    [Fact]
    public async Task An_invalid_opening_reports_every_error_at_once()
    {
        using var bank = new TestBank();

        var error = await Assert.ThrowsAsync<RequestValidationException>(() => bank.Send(new OpenAccount("", 1m)));

        Assert.Equal(["InitialDeposit", "Owner"], error.Errors.Select(e => e.Field).Order());
    }

    [Fact]
    public async Task A_transfer_moves_the_money_and_its_notification_reaches_the_recipient()
    {
        using var bank = new TestBank();

        Assert.Equal(new TransferResult(750m, 750m), await bank.Send(new Transfer(BankDb.Alice, BankDb.Bob, 250m)));

        var bob = await bank.Send(new GetAccount(BankDb.Bob));
        Assert.Equal(750m, bob.Balance);
        Assert.Equal("Transfer from Alice", bob.LatestOperations.Single().Label);
        Assert.Equal("You received 250 € from Alice.", bob.Notifications.Single());
        Assert.Empty(await bank.Send(new ListComplianceAlerts()));
    }

    [Theory]
    [InlineData(999, 0)]
    [InlineData(1000, 1)]
    public async Task A_transfer_of_1000_or_more_raises_a_compliance_alert(int amount, int expectedAlerts)
    {
        using var bank = new TestBank();

        await bank.Send(new Transfer(BankDb.Alice, BankDb.Bob, amount));

        Assert.Equal(expectedAlerts, (await bank.Send(new ListComplianceAlerts())).Count);
    }

    [Theory]
    [InlineData(5_001, "Amount")]   // the limit is a validation rule: refused even before the balance is read
    [InlineData(0, "Amount")]
    public async Task An_invalid_transfer_is_refused_by_its_validator(int amount, string field)
    {
        using var bank = new TestBank();

        var error = await Assert.ThrowsAsync<RequestValidationException>(() => bank.Send(new Transfer(BankDb.Alice, BankDb.Bob, amount)));

        Assert.Equal(field, error.Errors.Single().Field);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_refused_by_its_validator()
    {
        using var bank = new TestBank();

        var error = await Assert.ThrowsAsync<RequestValidationException>(() => bank.Send(new Transfer(BankDb.Alice, BankDb.Alice, 10m)));

        Assert.Equal("Target", error.Errors.Single().Field);
    }

    [Fact]
    public async Task A_failing_notification_handler_cancels_the_whole_transfer()
    {
        using var bank = new TestBank(services =>
            services.AddScoped<INotificationHandler<TransferCompleted>, FailingAuditHandler>());

        await Assert.ThrowsAsync<TimeoutException>(() => bank.Send(new Transfer(BankDb.Alice, BankDb.Bob, 250m)));

        var (alice, bob) = (await bank.Send(new GetAccount(BankDb.Alice)), await bank.Send(new GetAccount(BankDb.Bob)));
        Assert.Equal((1000m, 500m), (alice.Balance, bob.Balance));
        Assert.Empty(bob.Notifications);
    }

    [Fact]
    public async Task The_journal_records_each_request_with_its_outcome()
    {
        using var bank = new TestBank();

        await bank.Send(new Withdraw(BankDb.Alice, 100m));
        await Assert.ThrowsAsync<RequestValidationException>(() => bank.Send(new Withdraw(BankDb.Alice, 0m)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => bank.Send(new Withdraw(BankDb.Alice, 10_000m)));

        var journal = bank.Services.GetRequiredService<RequestJournal>().Latest(10);
        Assert.Equal(
            [("Withdraw", "ok"), ("Withdraw", "RequestValidationException"), ("Withdraw", "BusinessRuleException")],
            journal.Select(e => (e.Request, e.Outcome)));
    }

    private class FailingAuditHandler : INotificationHandler<TransferCompleted>
    {
        public Task Handle(TransferCompleted notification, CancellationToken ct) =>
            throw new TimeoutException("The audit service is down.");
    }
}
