using System.Collections.Concurrent;
using Clean.UseCases.Gateways;
using Clean.Entities;

namespace Clean.InterfaceAdapters.Gateways;

/// <summary>Implementation of the gateway (in memory here; an EF Core version would go in the same place).</summary>
public class InMemoryAccountsGateway : IAccountsGateway
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ConcurrentDictionary<Guid, (string Owner, decimal Balance)> _rows = new();

    public InMemoryAccountsGateway(params Account[] initialAccounts)
    {
        foreach (var account in initialAccounts)
            _rows[account.Id] = (account.Owner, account.Balance);
    }

    public static InMemoryAccountsGateway WithDemoData() =>
        new(new Account(Alice, "Alice", 1000m), new Account(Bob, "Bob", 500m));

    public Task<Account?> Load(Guid id) =>
        Task.FromResult(_rows.TryGetValue(id, out var row) ? new Account(id, row.Owner, row.Balance) : null);

    public Task Save(Account account)
    {
        _rows[account.Id] = (account.Owner, account.Balance);
        return Task.CompletedTask;
    }
}
