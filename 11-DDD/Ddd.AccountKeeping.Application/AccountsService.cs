using Ddd.AccountKeeping.Contracts;
using Ddd.AccountKeeping.Domain;
using Ddd.AccountKeeping.Domain.Accounts;
using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Application;

public interface IUnitOfWork
{
    Task Commit();
}

public record MovementView(DateTime Date, string Type, string Label, string Amount);

public record AccountView(
    Guid Id, string Iban, string Owner, decimal Balance, string Currency, string DisplayedBalance,
    string DailyWithdrawalLimit, string Status, string? BlockReason, IReadOnlyList<MovementView> LatestMovements);

/// <summary>
/// Application service: it translates the raw data (string, decimal) into domain objects,
/// orchestrates, then publishes the events once the changes are saved.
/// </summary>
public class AccountsService(
    IAccounts accounts,
    AccountFactory factory,
    TransferService transferService,
    IUnitOfWork unitOfWork,
    EventPublisher publisher,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AccountView> Open(string owner, decimal initialDeposit, string currency, Guid? id = null)
    {
        var account = await factory.Open(
            OwnerName.From(owner), new Money(initialDeposit, Currency.Of(currency)), Now,
            id is null ? null : new AccountId(id.Value));
        await accounts.Add(account);
        await Finish(account);
        return View(account);
    }

    public async Task<AccountView> Get(Guid id) => View(await Load(id));

    public async Task<IReadOnlyList<AccountView>> List() => (await accounts.All()).Select(View).ToList();

    public async Task<AccountView> Withdraw(Guid id, decimal amount)
    {
        var account = await Load(id);
        account.Withdraw(new Money(amount, account.Currency), Now);
        await Finish(account);
        return View(account);
    }

    public async Task<AccountView> Transfer(Guid sourceId, string beneficiaryIban, decimal amount)
    {
        var source = await Load(sourceId);
        var iban = Iban.From(beneficiaryIban);
        var target = await accounts.ByIban(iban) ?? throw new AccountNotFoundException($"no account of the bank has the IBAN {iban}");

        await transferService.Transfer(source, target, new Money(amount, source.Currency), Now);
        await Finish(source, target);
        return View(source);
    }

    public async Task Block(Guid id, string reason)
    {
        var account = await Load(id);
        account.Block(reason, Now);
        await Finish(account);
    }

    private async Task<Account> Load(Guid id) =>
        await accounts.ById(new AccountId(id)) ?? throw new AccountNotFoundException(id.ToString());

    /// <summary>Save first, publish next: a fact that was not saved is never announced.</summary>
    private async Task Finish(params Account[] changed)
    {
        await unitOfWork.Commit();
        var events = changed.SelectMany(c => c.ExtractEvents()).ToList();
        await publisher.Publish(events);
    }

    private static AccountView View(Account c) => new(
        c.Id.Value, c.Iban.ToString(), c.Owner.Value, c.Balance.Amount, c.Currency.Code, c.Balance.ToString(),
        c.DailyWithdrawalLimit.ToString(), c.Status.ToString(), c.BlockReason,
        c.Movements.OrderByDescending(m => m.Date).Take(10)
            .Select(m => new MovementView(m.Date, m.Type.ToString(), m.Label, m.Amount.ToString())).ToList());
}
