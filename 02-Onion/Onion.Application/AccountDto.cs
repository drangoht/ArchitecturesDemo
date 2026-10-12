using Onion.Domain.Model;

namespace Onion.Application;

/// <summary>What leaves the application layer: never the entity itself.</summary>
public record AccountDto(Guid Id, string Owner, decimal Balance)
{
    public static AccountDto From(Account account) => new(account.Id, account.Owner, account.Balance);
}
