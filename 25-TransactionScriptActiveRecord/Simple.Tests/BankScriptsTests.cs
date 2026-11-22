using Simple.Data;
using Simple.Scripts;

namespace Simple.Tests;

/// <summary>
/// A script is tested by calling it, then reading the database: there is no object to look at,
/// only rows. No fake is needed, but no test runs without a database either.
/// </summary>
public class BankScriptsTests
{
    private readonly Database _database = Database.WithDemoData();
    private BankScripts Scripts => new(_database);

    [Fact]
    public void Opening_an_account_inserts_the_account_and_its_initial_deposit()
    {
        var id = Scripts.OpenAccount("  Chloe ", 50m);

        var account = Scripts.GetAccount(id);
        Assert.Equal(("Chloe", 50m), (account.Owner, account.Balance));
        var operation = account.LatestOperations.Single();
        Assert.Equal(("Initial deposit", 50m), (operation.Label, operation.Amount));
    }

    [Theory]
    [InlineData("", 50)]
    [InlineData("Chloe", 9)]
    public void An_invalid_opening_is_refused(string owner, int initialDeposit)
    {
        Assert.Throws<ScriptRefusedException>(() => Scripts.OpenAccount(owner, initialDeposit));
    }

    [Fact]
    public void A_withdrawal_updates_the_balance_and_records_the_operation()
    {
        var result = Scripts.Withdraw(Database.Alice, 100m);

        Assert.Equal(new WithdrawalResult(Database.Alice, 900m), result);
        var alice = Scripts.GetAccount(Database.Alice);
        Assert.Equal(900m, alice.Balance);
        Assert.Equal(("Withdrawal", -100m), (alice.LatestOperations.Single().Label, alice.LatestOperations.Single().Amount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void A_refused_withdrawal_writes_nothing(int amount)
    {
        Assert.Throws<ScriptRefusedException>(() => Scripts.Withdraw(Database.Alice, amount));

        var alice = Scripts.GetAccount(Database.Alice);
        Assert.Equal(1000m, alice.Balance);
        Assert.Empty(alice.LatestOperations);
    }

    [Fact]
    public void A_transfer_moves_the_money_and_records_both_operations()
    {
        var result = Scripts.Transfer(Database.Alice, Database.Bob, 250m);

        Assert.Equal(new TransferResult(750m, 750m), result);
        var (alice, bob) = (Scripts.GetAccount(Database.Alice), Scripts.GetAccount(Database.Bob));
        Assert.Equal((750m, 750m), (alice.Balance, bob.Balance));
        Assert.Equal("Transfer to Bob", alice.LatestOperations.Single().Label);
        Assert.Equal("Transfer from Alice", bob.LatestOperations.Single().Label);
    }

    [Theory]
    [InlineData(1_001)]   // insufficient balance (copy of the Withdraw check)
    [InlineData(0)]       // not strictly positive (copy of the Withdraw check)
    public void A_transfer_breaking_a_rule_is_refused(int amount)
    {
        Assert.Throws<ScriptRefusedException>(() => Scripts.Transfer(Database.Alice, Database.Bob, amount));

        Assert.Equal(1000m, Scripts.GetAccount(Database.Alice).Balance);
    }

    [Fact]
    public void A_transfer_above_the_limit_is_refused_even_with_enough_money()
    {
        var rich = Scripts.OpenAccount("Rich", 20_000m);

        Assert.Throws<ScriptRefusedException>(() => Scripts.Transfer(rich, Database.Bob, 5_001m));
        Assert.Equal(15_000m, Scripts.Transfer(rich, Database.Bob, 5_000m).SourceBalance);
    }

    [Fact]
    public void A_transfer_to_the_same_account_is_refused()
    {
        Assert.Throws<ScriptRefusedException>(() => Scripts.Transfer(Database.Alice, Database.Alice, 10m));
    }

    [Fact]
    public void A_transfer_to_an_unknown_account_rolls_back_the_debit_already_written()
    {
        var unknown = Guid.NewGuid();

        var error = Assert.Throws<AccountNotFoundException>(() => Scripts.Transfer(Database.Alice, unknown, 50m));

        Assert.Contains(unknown.ToString(), error.Message);
        var alice = Scripts.GetAccount(Database.Alice);
        Assert.Equal(1000m, alice.Balance);
        Assert.Empty(alice.LatestOperations);
    }

    [Fact]
    public void Only_the_five_latest_operations_are_returned_newest_first()
    {
        var clock = new ManualClock();
        var scripts = new BankScripts(Database.WithDemoData(clock));
        for (var i = 1; i <= 7; i++)
        {
            clock.Advance();
            scripts.Withdraw(Database.Alice, i);
        }

        var amounts = scripts.GetAccount(Database.Alice).LatestOperations.Select(o => o.Amount);

        Assert.Equal([-7m, -6m, -5m, -4m, -3m], amounts);
    }

    [Fact]
    public void An_unknown_account_throws_AccountNotFound()
    {
        Assert.Throws<AccountNotFoundException>(() => Scripts.GetAccount(Guid.NewGuid()));
        Assert.Throws<AccountNotFoundException>(() => Scripts.Withdraw(Guid.NewGuid(), 10m));
    }
}
