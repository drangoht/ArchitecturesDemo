using Microsoft.EntityFrameworkCore;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Infrastructure;

public class AccountsRepository(BankDbContext db) : IAccountsRepository
{
    public async Task<Account?> ById(Guid id) => await db.Accounts.FindAsync(id);

    public async Task Add(Account account) => await db.Accounts.AddAsync(account);

    public async Task<bool> IsEmpty() => !await db.Accounts.AnyAsync();
}
