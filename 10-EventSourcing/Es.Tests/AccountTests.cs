using Es.Domain;

namespace Es.Tests;

/// <summary>
/// The natural test style with Event Sourcing:
/// GIVEN past events, WHEN a command is sent, THEN these new events are expected.
/// </summary>
public class AccountTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTime T = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<IEvent> When(IEvent[] given, Action<Account> command)
    {
        var account = Account.Rehydrate(given);
        command(account);
        return account.NewEvents;
    }

    private static readonly IEvent[] AccountWith100Euros = [new AccountOpened(Id, "Alice", 100m, T)];

    [Fact]
    public void A_possible_withdrawal_produces_MoneyWithdrawn()
    {
        var then = When(AccountWith100Euros, c => c.Withdraw(30m, "Groceries", T));

        Assert.Equal([new MoneyWithdrawn(Id, 30m, "Groceries", T)], then);
    }

    [Fact]
    public void A_withdrawal_above_the_balance_is_refused()
    {
        Assert.Throws<BusinessRuleException>(() => When(AccountWith100Euros, c => c.Withdraw(130m, "Groceries", T)));
    }

    [Fact]
    public void The_state_is_deduced_from_the_events()
    {
        var account = Account.Rehydrate(
        [
            new AccountOpened(Id, "Alice", 100m, T),
            new MoneyDeposited(Id, 50m, T),
            new MoneyWithdrawn(Id, 30m, "Groceries", T),
            new TransferReceived(Id, Guid.NewGuid(), Guid.NewGuid(), 5m, T)
        ]);

        Assert.Equal((125m, 4), (account.Balance, account.Version));
    }

    [Fact]
    public void An_account_can_only_be_closed_if_its_balance_is_zero()
    {
        Assert.Throws<BusinessRuleException>(() => When(AccountWith100Euros, c => c.Close(T)));

        var then = When([.. AccountWith100Euros, new MoneyWithdrawn(Id, 100m, "Balance", T)], c => c.Close(T));

        Assert.Equal([new AccountClosed(Id, T)], then);
    }

    [Fact]
    public void A_closed_account_refuses_any_operation()
    {
        IEvent[] given = [new AccountOpened(Id, "Alice", 10m, T), new MoneyWithdrawn(Id, 10m, "Balance", T), new AccountClosed(Id, T)];

        var error = Assert.Throws<BusinessRuleException>(() => When(given, c => c.Deposit(20m, T)));

        Assert.Equal("This account is closed.", error.Message);
    }
}
