using Onion.Domain.Model;

namespace Onion.Domain.Interfaces;

// The data access interfaces are declared at the center;
// the infrastructure (outer layer) implements them: dependencies point inwards.

public interface IAccountsRepository
{
    Task<Account?> ById(Guid id);
    Task Add(Account account);
    Task<bool> IsEmpty();
}

/// <summary>Commits at once all the changes made during a use case.</summary>
public interface IUnitOfWork
{
    Task Save();
}
