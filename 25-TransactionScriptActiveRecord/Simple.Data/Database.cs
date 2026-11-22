namespace Simple.Data;

/// <summary>
/// Stands in for a SQL database: tables of rows and transactions, nothing more.
/// Both styles of the demo work on it, the way they would work on SQL Server through ADO.NET.
/// </summary>
public sealed class Database(TimeProvider? clock = null)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // One transaction at a time, like the SERIALIZABLE isolation level (in a much cruder way):
    // two withdrawals can never read the same balance and both write it.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private Dictionary<Guid, AccountRow> _accounts = [];
    private Dictionary<Guid, OperationRow> _operations = [];

    public static Database WithDemoData(TimeProvider? clock = null)
    {
        var database = new Database(clock);
        using var tx = database.Begin();
        tx.Accounts.Insert(new AccountRow(Alice, "Alice", 1000m));
        tx.Accounts.Insert(new AccountRow(Bob, "Bob", 500m));
        tx.Commit();
        return database;
    }

    /// <summary>Waits for the running transaction to finish, then works on a copy of the tables.</summary>
    public Transaction Begin()
    {
        _gate.Wait();
        return new Transaction(this, new(_accounts), new(_operations), _clock.GetUtcNow().UtcDateTime);
    }

    internal void Publish(Dictionary<Guid, AccountRow> accounts, Dictionary<Guid, OperationRow> operations)
    {
        _accounts = accounts;
        _operations = operations;
    }

    internal void Release() => _gate.Release();
}

/// <summary>
/// The changes are made on copies of the tables: Commit publishes them,
/// Dispose without Commit throws them away (rollback).
/// </summary>
public sealed class Transaction : IDisposable
{
    private readonly Database _database;
    private bool _finished;

    internal Transaction(Database database, Dictionary<Guid, AccountRow> accounts,
        Dictionary<Guid, OperationRow> operations, DateTime now)
    {
        _database = database;
        Accounts = new Table<AccountRow>("accounts", accounts);
        Operations = new Table<OperationRow>("operations", operations);
        Now = now;
    }

    public Table<AccountRow> Accounts { get; }
    public Table<OperationRow> Operations { get; }

    /// <summary>The date of the transaction, used for every row it writes.</summary>
    public DateTime Now { get; }

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _database.Publish(Accounts.Rows, Operations.Rows);
        Finish();
    }

    public void Dispose()
    {
        if (!_finished)
            Finish();
    }

    private void Finish()
    {
        _finished = true;
        _database.Release();
    }
}
