using Microsoft.EntityFrameworkCore;

namespace Layered.Data;

// The interface is defined BY the data layer, in ITS vocabulary (AccountEntity).
// Key difference with Onion/Hexagonal: there, the business code defines the interface it needs.

public interface IAccountsDao
{
    Task<AccountEntity?> ById(Guid id);
    Task Add(AccountEntity account);
    Task<bool> IsEmpty();
    Task Save();
}

public class AccountsDao(BankDbContext db) : IAccountsDao
{
    public async Task<AccountEntity?> ById(Guid id) => await db.Accounts.FindAsync(id);

    public async Task Add(AccountEntity account) => await db.Accounts.AddAsync(account);

    public async Task<bool> IsEmpty() => !await db.Accounts.AnyAsync();

    public Task Save() => db.SaveChangesAsync();
}
