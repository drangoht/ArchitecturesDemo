using System.Collections.Concurrent;
using Es.Domain;

namespace Es.Storage;

/// <summary>
/// Snapshots: a mere cache of an account's state at a given version.
/// They can be cleared at any time, nothing is lost: they are recomputed from the events.
/// </summary>
public class SnapshotStore
{
    private readonly ConcurrentDictionary<Guid, AccountSnapshot> _snapshots = new();

    public AccountSnapshot? Latest(Guid accountId) => _snapshots.GetValueOrDefault(accountId);
    public void Save(AccountSnapshot snapshot) => _snapshots[snapshot.Id] = snapshot;
    public void ClearAll() => _snapshots.Clear();
}

/// <summary>Loads an account by replaying its events, and saves its new events.</summary>
public class AccountRepository(IEventStore store, SnapshotStore snapshots)
{
    /// <summary>A snapshot is taken every N events.</summary>
    public const int SnapshotFrequency = 5;

    public async Task<Account> Load(Guid id)
    {
        var snapshot = snapshots.Latest(id);
        var rest = await store.ReadStream(id, afterVersion: snapshot?.Version ?? 0);
        if (snapshot is null && rest.Count == 0)
            throw new AccountNotFoundException(id);
        return Account.FromSnapshot(snapshot, rest.Select(e => e.Event()));
    }

    /// <summary>Time travel: the state of the account as it was at a given version.</summary>
    public async Task<Account> LoadAtVersion(Guid id, int version)
    {
        var history = await store.ReadStream(id);
        if (history.Count == 0)
            throw new AccountNotFoundException(id);
        return Account.Rehydrate(history.Where(e => e.Version <= version).Select(e => e.Event()));
    }

    /// <summary>Time travel: the state of the account as it was at a given date.</summary>
    public async Task<Account> LoadAtDate(Guid id, DateTime date)
    {
        var history = await store.ReadStream(id);
        if (history.Count == 0)
            throw new AccountNotFoundException(id);
        return Account.Rehydrate(history.Where(e => e.Date <= date).Select(e => e.Event()));
    }

    /// <summary>Saves the new events of one or more accounts, all or nothing.</summary>
    public async Task Save(params Account[] accounts)
    {
        var toSave = accounts.Where(c => c.NewEvents.Count > 0).ToList();
        await store.Append(toSave
            .Select(c => new ToWrite(c.Id, c.LoadedVersion, c.NewEvents.ToList()))
            .ToArray());

        foreach (var account in toSave)
        {
            account.MarkSaved();
            var latestSnapshot = snapshots.Latest(account.Id)?.Version ?? 0;
            if (account.Version - latestSnapshot >= SnapshotFrequency)
                snapshots.Save(account.Snapshot());
        }
    }
}
