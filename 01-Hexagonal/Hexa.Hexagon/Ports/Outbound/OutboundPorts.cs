using Hexa.Hexagon.Domain;

namespace Hexa.Hexagon.Ports.Outbound;

// Outbound ports ("driven", secondary): what the application NEEDS.
// The hexagon defines the interface; the outbound adapters (memory, JSON file, SQL…) implement it.

public interface IAccounts
{
    Task<Account?> Load(Guid id);
    Task Save(Account account);
}

public interface INotifications
{
    Task WithdrawalMade(Guid accountId, decimal amount);
    Task TransferMade(Guid sourceId, Guid targetId, decimal amount);
}
