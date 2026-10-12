using Microsoft.EntityFrameworkCore;

namespace Components.Accounts;

internal class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
}
