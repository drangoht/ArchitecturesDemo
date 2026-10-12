using Ddd.AccountKeeping.Application;
using Ddd.AccountKeeping.Domain.Accounts;
using Ddd.AccountKeeping.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Ddd.AccountKeeping.Infrastructure;

/// <summary>
/// The mapping lives here, not in the domain: the value objects are converted for the database.
/// (With SQL Server, ComplexProperty/OwnsOne would be preferred for Money, to be able to query on the amount.)
/// </summary>
public class AccountKeepingDb(DbContextOptions<AccountKeepingDb> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Account> Accounts => Set<Account>();

    public Task Commit() => SaveChangesAsync();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(account =>
        {
            account.HasKey(c => c.Id);
            account.Property(c => c.Id).HasConversion(id => id.Value, value => new AccountId(value));
            account.Property(c => c.Iban).HasConversion(iban => iban.Value, value => Iban.From(value));
            account.Property(c => c.Owner).HasConversion(name => name.Value, value => OwnerName.From(value));
            account.Property(c => c.Currency).HasConversion(currency => currency.Code, code => Currency.Of(code));
            account.Property(c => c.Balance).HasConversion(money => money.ToText(), text => Money.FromText(text));
            account.Property(c => c.DailyWithdrawalLimit).HasConversion(money => money.ToText(), text => Money.FromText(text));
            account.Ignore(c => c.DomainEvents);

            // The movements belong to the aggregate: they are loaded and saved with it.
            account.OwnsMany(c => c.Movements, movement =>
            {
                movement.WithOwner().HasForeignKey("AccountId");
                movement.HasKey(m => m.Id);
                movement.Property(m => m.Id).ValueGeneratedNever();
                movement.Property(m => m.Amount).HasConversion(money => money.ToText(), text => Money.FromText(text));
            });
            account.Navigation(c => c.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

public class AccountRepository(AccountKeepingDb db) : IAccounts
{
    public Task<Account?> ById(AccountId id) => db.Accounts.FirstOrDefaultAsync(c => c.Id == id);

    public Task<Account?> ByIban(Iban iban) => db.Accounts.FirstOrDefaultAsync(c => c.Iban == iban);

    public async Task<IReadOnlyList<Account>> All() => await db.Accounts.ToListAsync();

    public async Task Add(Account account) => await db.Accounts.AddAsync(account);

    public async Task<long> NextAccountNumber() =>
        await db.Accounts.CountAsync() + db.ChangeTracker.Entries<Account>().Count(e => e.State == EntityState.Added) + 1;
}
