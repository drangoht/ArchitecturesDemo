using Simple.Data;
using Simple.Records;

namespace Simple.Tests;

/// <summary>
/// The rules are on the object, but the object cannot exist without a transaction:
/// even a test of "insufficient balance" opens a database.
/// </summary>
public class AccountRecordTests
{
    private readonly Database _database = Database.WithDemoData();

    [Fact]
    public void Find_loads_the_row_and_returns_null_for_an_unknown_id()
    {
        using var tx = _database.Begin();

        var alice = Account.Find(tx, Database.Alice);

        Assert.Equal(("Alice", 1000m), (alice!.Owner, alice.Balance));
        Assert.Null(Account.Find(tx, Guid.NewGuid()));
    }

    [Fact]
    public void A_change_that_is_not_saved_is_lost()
    {
        using (var tx = _database.Begin())
        {
            Account.Find(tx, Database.Alice)!.Withdraw(100m);
            tx.Commit();   // Save() was forgotten: the object changed, not the row
        }

        Assert.Equal(1000m, BalanceOf(Database.Alice));
    }

    [Fact]
    public void Save_writes_the_row_and_the_recorded_operations()
    {
        using (var tx = _database.Begin())
        {
            var alice = Account.Find(tx, Database.Alice)!;
            alice.Withdraw(100m);
            alice.Save();
            tx.Commit();
        }

        using var check = _database.Begin();
        var reloaded = Account.Find(check, Database.Alice)!;
        Assert.Equal(900m, reloaded.Balance);
        var operation = reloaded.LatestOperations().Single();
        Assert.Equal(("Withdrawal", -100m), (operation.Label, operation.Amount));
    }

    [Fact]
    public void Saving_twice_does_not_record_the_operations_twice()
    {
        using var tx = _database.Begin();
        var alice = Account.Find(tx, Database.Alice)!;
        alice.Withdraw(100m);

        alice.Save();
        alice.Save();

        Assert.Single(alice.LatestOperations());
    }

    [Fact]
    public void A_new_account_is_inserted_by_its_first_Save()
    {
        Guid id;
        using (var tx = _database.Begin())
        {
            var account = Account.Open(tx, " Chloe ", 50m);
            id = account.Id;
            Assert.Null(Account.Find(tx, id));   // not in the table before Save
            account.Save();
            tx.Commit();
        }

        using var check = _database.Begin();
        var chloe = Account.Find(check, id)!;
        Assert.Equal(("Chloe", 50m), (chloe.Owner, chloe.Balance));
        Assert.Equal("Initial deposit", chloe.LatestOperations().Single().Label);
    }

    [Theory]
    [InlineData("", 50)]
    [InlineData("Chloe", 9)]
    public void An_invalid_opening_is_refused(string owner, int initialDeposit)
    {
        using var tx = _database.Begin();

        Assert.Throws<RuleViolationException>(() => Account.Open(tx, owner, initialDeposit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void A_refused_withdrawal_leaves_the_object_unchanged(int amount)
    {
        using var tx = _database.Begin();
        var alice = Account.Find(tx, Database.Alice)!;

        Assert.Throws<RuleViolationException>(() => alice.Withdraw(amount));

        alice.Save();
        Assert.Equal(1000m, alice.Balance);
        Assert.Empty(alice.LatestOperations());
    }

    [Fact]
    public void A_transfer_moves_the_money_between_the_two_objects()
    {
        using (var tx = _database.Begin())
        {
            var alice = Account.Find(tx, Database.Alice)!;
            var bob = Account.Find(tx, Database.Bob)!;
            alice.TransferTo(bob, 250m);
            alice.Save();
            bob.Save();
            tx.Commit();
        }

        Assert.Equal((750m, 750m), (BalanceOf(Database.Alice), BalanceOf(Database.Bob)));
        using var check = _database.Begin();
        Assert.Equal("Transfer from Alice", Account.Find(check, Database.Bob)!.LatestOperations().Single().Label);
    }

    [Fact]
    public void A_transfer_above_the_limit_is_refused_even_with_enough_money()
    {
        using var tx = _database.Begin();
        var rich = Account.Open(tx, "Rich", 20_000m);
        var bob = Account.Find(tx, Database.Bob)!;

        Assert.Throws<RuleViolationException>(() => rich.TransferTo(bob, 5_001m));
        rich.TransferTo(bob, 5_000m);
        Assert.Equal(15_000m, rich.Balance);
    }

    [Fact]
    public void A_saved_new_account_is_updated_by_the_next_Save()
    {
        Guid id;
        using (var tx = _database.Begin())
        {
            var account = Account.Open(tx, "Chloe", 50m);
            id = account.Id;
            account.Save();
            account.Withdraw(20m);
            account.Save();
            tx.Commit();
        }

        Assert.Equal(30m, BalanceOf(id));
    }

    [Theory]
    [InlineData(1_001)]   // insufficient balance: the same Debit as Withdraw, written once
    [InlineData(0)]
    public void A_transfer_breaking_a_rule_changes_neither_account(int amount)
    {
        using var tx = _database.Begin();
        var alice = Account.Find(tx, Database.Alice)!;
        var bob = Account.Find(tx, Database.Bob)!;

        Assert.Throws<RuleViolationException>(() => alice.TransferTo(bob, amount));

        Assert.Equal((1000m, 500m), (alice.Balance, bob.Balance));
    }

    [Fact]
    public void A_transfer_to_the_same_account_is_refused()
    {
        using var tx = _database.Begin();
        var alice = Account.Find(tx, Database.Alice)!;
        var aliceAgain = Account.Find(tx, Database.Alice)!;

        Assert.Throws<RuleViolationException>(() => alice.TransferTo(aliceAgain, 10m));
    }

    private decimal BalanceOf(Guid id)
    {
        using var tx = _database.Begin();
        return Account.Find(tx, id)!.Balance;
    }
}
