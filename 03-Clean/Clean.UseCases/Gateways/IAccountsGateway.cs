using Clean.Entities;

namespace Clean.UseCases.Gateways;

/// <summary>
/// Gateway: the data access the use cases need.
/// Defined here, implemented in the Interface Adapters circle.
/// </summary>
public interface IAccountsGateway
{
    Task<Account?> Load(Guid id);
    Task Save(Account account);
}
