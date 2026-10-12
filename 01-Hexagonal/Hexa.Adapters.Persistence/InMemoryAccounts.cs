using System.Collections.Concurrent;
using Hexa.Hexagon.Domain;
using Hexa.Hexagon.Ports.Outbound;

namespace Hexa.Adapters.Persistence;

/// <summary>
/// In-memory outbound adapter: ideal for tests and demos.
/// It stores copies, like a real database: an unsaved change is not visible.
/// </summary>
public class InMemoryAccounts : IAccounts
{
    private readonly ConcurrentDictionary<Guid, (string Owner, decimal Balance)> _rows = new();

    public InMemoryAccounts(IEnumerable<Account>? initialAccounts = null)
    {
        foreach (var account in initialAccounts ?? Enumerable.Empty<Account>())
            _rows[account.Id] = (account.Owner, account.Balance);
    }

    public Task<Account?> Load(Guid id) =>
        Task.FromResult(_rows.TryGetValue(id, out var row)
            ? new Account(id, row.Owner, row.Balance)
            : null);

    public Task Save(Account account)
    {
        _rows[account.Id] = (account.Owner, account.Balance);
        return Task.CompletedTask;
    }
}
