namespace Components.Accounts;

// The implementation is internal: it can only be obtained through the interface, via dependency injection.
internal class AccountsComponent(AccountsDbContext db) : IAccountsComponent
{
    public async Task<AccountDto> Open(string owner, decimal initialDeposit, Guid? id = null)
    {
        var account = new Account(id ?? Guid.NewGuid(), owner, initialDeposit);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.ToDto();
    }

    public async Task<AccountDto?> Get(Guid accountId) =>
        (await db.Accounts.FindAsync(accountId))?.ToDto();

    public Task<Result> Withdraw(Guid accountId, decimal amount) =>
        Modify(accountId, account => account.Withdraw(amount));

    public Task<Result> Deposit(Guid accountId, decimal amount) =>
        Modify(accountId, account => account.Deposit(amount));

    private async Task<Result> Modify(Guid accountId, Func<Account, Result> operation)
    {
        var account = await db.Accounts.FindAsync(accountId);
        if (account is null)
            return Result.NotFound(accountId);

        var result = operation(account);
        if (result.Success)
            await db.SaveChangesAsync();
        return result;
    }
}
