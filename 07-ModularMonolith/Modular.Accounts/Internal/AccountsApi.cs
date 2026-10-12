using Modular.Accounts.Contracts;

namespace Modular.Accounts;

/// <summary>(Internal) implementation of the module's public API, used by the other modules.</summary>
internal class AccountsApi(AccountsDbContext db) : IAccountsApi
{
    public async Task<AccountInfo?> Get(Guid accountId) =>
        await db.Accounts.FindAsync(accountId) is { } c ? new AccountInfo(c.Id, c.Owner, c.Balance) : null;

    public Task<OperationResult> Debit(Guid accountId, decimal amount) =>
        Modify(accountId, account => account.Debit(amount));

    public Task<OperationResult> Credit(Guid accountId, decimal amount) =>
        Modify(accountId, account => account.Credit(amount));

    private async Task<OperationResult> Modify(Guid accountId, Func<Account, OperationResult> operation)
    {
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null)
            return OperationResult.NotFound(accountId);

        var result = operation(account);
        if (result.Success)
            await db.SaveChangesAsync();
        return result;
    }
}
