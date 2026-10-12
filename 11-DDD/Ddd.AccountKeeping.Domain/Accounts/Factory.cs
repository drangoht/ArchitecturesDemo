using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Domain.Accounts;

/// <summary>
/// Repository: one per aggregate root, with the domain's vocabulary.
/// It loads and saves WHOLE aggregates (account + movements), never pieces.
/// </summary>
public interface IAccounts
{
    Task<Account?> ById(AccountId id);
    Task<Account?> ByIban(Iban iban);
    Task<IReadOnlyList<Account>> All();
    Task Add(Account account);
    Task<long> NextAccountNumber();
}

/// <summary>
/// Factory: opening an account takes several things (number, IBAN computation, limit depending on the currency).
/// This creation logic belongs neither to the account itself nor to the application code.
/// </summary>
public class AccountFactory(IAccounts accounts)
{
    public const string BankCode = "30001";
    public const string BranchCode = "00001";

    public static readonly Money MinimumDeposit = new(10m, Currency.Euro);

    public async Task<Account> Open(OwnerName owner, Money initialDeposit, DateTime date, AccountId? id = null)
    {
        if (initialDeposit.Amount < 10m)
            throw new BusinessRuleException($"The initial deposit must be at least 10 {initialDeposit.Currency}.");

        var number = await accounts.NextAccountNumber();
        var iban = Iban.Generate("FR", $"{BankCode}{BranchCode}{number:D11}00");
        var limit = new Money(1_000m, initialDeposit.Currency);

        return Account.Open(id ?? AccountId.New(), iban, owner, initialDeposit, limit, date);
    }
}
