using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Application;

/// <summary>
/// Application service: loads, delegates to the domain, saves.
/// It contains no business rule; it coordinates.
/// </summary>
public class AccountsAppService(IAccountsRepository accounts, IUnitOfWork unitOfWork)
{
    public async Task<AccountDto> Get(Guid id) =>
        AccountDto.From(await accounts.GetOrThrow(id));

    public async Task<AccountDto> Withdraw(Guid id, decimal amount)
    {
        var account = await accounts.GetOrThrow(id);
        account.Withdraw(amount);
        await unitOfWork.Save();
        return AccountDto.From(account);
    }
}

internal static class AccountsRepositoryExtensions
{
    public static async Task<Account> GetOrThrow(this IAccountsRepository accounts, Guid id) =>
        await accounts.ById(id) ?? throw new AccountNotFoundException(id);
}
