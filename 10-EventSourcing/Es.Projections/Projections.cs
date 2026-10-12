using Es.Domain;
using Es.Storage;

namespace Es.Projections;

public interface IProjection
{
    void Apply(StoredEvent stored, IEvent @event);
    void Clear();
}

// ─── Statements: the current state of each account, ready to display ───

public record StatementView(Guid Id, string Owner, decimal Balance, bool Closed, int OperationCount, int Version);

public class StatementsProjection : IProjection
{
    private readonly Dictionary<Guid, StatementView> _statements = [];

    public StatementView? Get(Guid id) => _statements.GetValueOrDefault(id);

    public void Apply(StoredEvent stored, IEvent @event)
    {
        if (@event is AccountOpened o)
        {
            _statements[o.AccountId] = new StatementView(o.AccountId, o.Owner, o.InitialDeposit, false, 1, stored.Version);
            return;
        }

        var r = _statements[@event.AccountId];
        var balance = @event switch
        {
            MoneyDeposited e => r.Balance + e.Amount,
            MoneyWithdrawn e => r.Balance - e.Amount,
            TransferSent e => r.Balance - e.Amount,
            TransferReceived e => r.Balance + e.Amount,
            _ => r.Balance
        };
        _statements[@event.AccountId] = r with
        {
            Balance = balance,
            Closed = r.Closed || @event is AccountClosed,
            OperationCount = r.OperationCount + 1,
            Version = stored.Version
        };
    }

    public void Clear() => _statements.Clear();
}

// ─── Largest withdrawals: a projection added AFTERWARDS ───
// Since it is built by replaying the whole stream, it knows all the past withdrawals right away,
// including those made before it existed. Impossible with a classic database, which kept only the final state.

public record WithdrawalView(string Owner, decimal Amount, string Reason, DateTime Date);

public class LargestWithdrawalsProjection : IProjection
{
    private readonly Dictionary<Guid, string> _owners = [];
    private readonly List<WithdrawalView> _withdrawals = [];

    public IReadOnlyList<WithdrawalView> Top(int count) =>
        _withdrawals.OrderByDescending(r => r.Amount).ThenBy(r => r.Date).Take(count).ToList();

    public void Apply(StoredEvent stored, IEvent @event)
    {
        switch (@event)
        {
            case AccountOpened e:
                _owners[e.AccountId] = e.Owner;
                break;
            case MoneyWithdrawn e:
                _withdrawals.Add(new WithdrawalView(_owners[e.AccountId], e.Amount, e.Reason, e.Date));
                break;
        }
    }

    public void Clear()
    {
        _owners.Clear();
        _withdrawals.Clear();
    }
}

/// <summary>
/// Moves all the projections forward on the global stream, from the last processed position.
/// All the reads go through this engine, protected by a lock (the projections are in memory).
/// </summary>
public class ProjectionEngine(IEventStore store, StatementsProjection statements, LargestWithdrawalsProjection withdrawals)
{
    private readonly IProjection[] _projections = [statements, withdrawals];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public long Position { get; private set; }

    public async Task<int> CatchUp()
    {
        await _lock.WaitAsync();
        try
        {
            var newEvents = await store.ReadAll(Position);
            foreach (var stored in newEvents)
            {
                var @event = stored.Event();
                foreach (var projection in _projections)
                    projection.Apply(stored, @event);
                Position = stored.Position;
            }
            return newEvents.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Clears all the projections and replays the whole history from the first event.</summary>
    public async Task<int> Rebuild()
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var projection in _projections)
                projection.Clear();
            Position = 0;
        }
        finally
        {
            _lock.Release();
        }
        return await CatchUp();
    }

    public async Task<T> Read<T>(Func<T> read)
    {
        await _lock.WaitAsync();
        try
        {
            return read();
        }
        finally
        {
            _lock.Release();
        }
    }
}
