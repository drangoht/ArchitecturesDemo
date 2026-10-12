using Es.Domain;
using Es.Storage;

namespace Es.Tests;

public class StorageTests
{
    private static readonly DateTime T = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Two_writes_based_on_the_same_version_the_second_is_refused()
    {
        var store = new EventStore();
        var id = Guid.NewGuid();
        await store.Append(new ToWrite(id, 0, [new AccountOpened(id, "Alice", 100m, T)]));

        // Two users loaded the account at version 1 and each decide on a withdrawal.
        await store.Append(new ToWrite(id, 1, [new MoneyWithdrawn(id, 80m, "A", T)]));
        var second = () => store.Append(new ToWrite(id, 1, [new MoneyWithdrawn(id, 80m, "B", T)]));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(second);
        Assert.Equal(2, (await store.ReadStream(id)).Count);
    }

    [Fact]
    public async Task A_write_on_several_streams_is_all_or_nothing()
    {
        var store = new EventStore();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await store.Append(
            new ToWrite(a, 0, [new AccountOpened(a, "A", 100m, T)]),
            new ToWrite(b, 0, [new AccountOpened(b, "B", 100m, T)]));

        // The expected version of b is wrong: nothing is written, not even for a.
        var transfer = () => store.Append(
            new ToWrite(a, 1, [new TransferSent(a, Guid.NewGuid(), b, 10m, T)]),
            new ToWrite(b, 5, [new TransferReceived(b, Guid.NewGuid(), a, 10m, T)]));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(transfer);
        Assert.Equal(2, (await store.ReadAll()).Count);
    }

    [Fact]
    public async Task The_file_is_enough_to_find_everything_again_after_a_restart()
    {
        var file = Path.Combine(Path.GetTempPath(), $"events-{Guid.NewGuid()}.jsonl");
        try
        {
            var id = Guid.NewGuid();
            var before = new EventStore(file);
            await before.Append(new ToWrite(id, 0, [new AccountOpened(id, "Alice", 100m, T), new MoneyWithdrawn(id, 40m, "Groceries", T)]));

            var after = new EventStore(file);  // "restart"

            var account = Account.Rehydrate((await after.ReadStream(id)).Select(e => e.Event()));
            Assert.Equal(60m, account.Balance);
            Assert.Equal(2, File.ReadAllLines(file).Length);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void An_old_event_format_is_converted_when_read()
    {
        // Withdrawal recorded by an old version of the application, before the reason was added.
        const string jsonV1 = """{"accountId":"11111111-1111-1111-1111-111111111111","amount":40,"date":"2025-01-15T10:00:00Z"}""";

        var @event = EventSerializer.Deserialize("MoneyWithdrawn", jsonV1);

        var withdrawal = Assert.IsType<MoneyWithdrawn>(@event);
        Assert.Equal((40m, "Not specified"), (withdrawal.Amount, withdrawal.Reason));
    }

    [Fact]
    public async Task A_snapshot_avoids_replaying_the_whole_history_without_changing_the_result()
    {
        var store = new SpyStore(new EventStore());
        var repository = new AccountRepository(store, new SnapshotStore());
        var id = Guid.NewGuid();
        await repository.Save(Account.Open(id, "Alice", 1000m, T));
        for (var i = 1; i <= 11; i++)
        {
            var account = await repository.Load(id);
            account.Withdraw(10m, $"Withdrawal {i}", T);
            await repository.Save(account);
        }

        store.EventsRead = 0;
        var withSnapshot = await repository.Load(id);
        var withoutSnapshot = Account.Rehydrate((await store.ReadStream(id)).Select(e => e.Event()));

        Assert.Equal(withoutSnapshot.Snapshot(), withSnapshot.Snapshot());
        Assert.Equal(12, withSnapshot.Version);
        Assert.Equal(2 + 12, store.EventsRead);  // 2 read after the v10 snapshot, then 12 for the comparison
    }

    private class SpyStore(IEventStore inner) : IEventStore
    {
        public int EventsRead { get; set; }

        public async Task<IReadOnlyList<StoredEvent>> ReadStream(Guid stream, int afterVersion = 0)
        {
            var read = await inner.ReadStream(stream, afterVersion);
            EventsRead += read.Count;
            return read;
        }

        public Task<IReadOnlyList<StoredEvent>> ReadAll(long afterPosition = 0, int maximum = int.MaxValue) =>
            inner.ReadAll(afterPosition, maximum);

        public Task Append(params ToWrite[] writes) => inner.Append(writes);
    }
}
