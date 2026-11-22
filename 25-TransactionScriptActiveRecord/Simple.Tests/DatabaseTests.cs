using Simple.Data;

namespace Simple.Tests;

// The transaction is what makes a script safe: everything or nothing, and one script at a time.
public class DatabaseTests
{
    [Fact]
    public void Committed_changes_are_visible_to_the_next_transaction()
    {
        var database = Database.WithDemoData();

        using (var tx = database.Begin())
        {
            tx.Accounts.Update(new AccountRow(Database.Alice, "Alice", 1m));
            tx.Commit();
        }

        using var next = database.Begin();
        Assert.Equal(1m, next.Accounts.Find(Database.Alice)!.Balance);
    }

    [Fact]
    public void A_transaction_disposed_without_commit_is_rolled_back()
    {
        var database = Database.WithDemoData();

        using (var tx = database.Begin())
        {
            tx.Accounts.Update(new AccountRow(Database.Alice, "Alice", 1m));
            tx.Operations.Insert(new OperationRow(Guid.NewGuid(), Database.Alice, "Lost", -999m, tx.Now));
        }

        using var next = database.Begin();
        Assert.Equal(1000m, next.Accounts.Find(Database.Alice)!.Balance);
        Assert.Empty(next.Operations.Where(_ => true));
    }

    [Fact]
    public void Inserting_an_existing_key_or_updating_a_missing_one_fails()
    {
        var database = Database.WithDemoData();
        using var tx = database.Begin();

        Assert.Throws<InvalidOperationException>(() => tx.Accounts.Insert(new AccountRow(Database.Bob, "Bob", 0m)));
        Assert.Throws<InvalidOperationException>(() => tx.Accounts.Update(new AccountRow(Guid.NewGuid(), "Nobody", 0m)));
    }

    [Fact]
    public async Task A_second_transaction_waits_for_the_first_one_to_finish()
    {
        var database = Database.WithDemoData();
        var first = database.Begin();

        var second = Task.Run(database.Begin);
        var finishedFirst = await Task.WhenAny(second, Task.Delay(200));
        Assert.NotSame(second, finishedFirst);

        first.Dispose();
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }
}
