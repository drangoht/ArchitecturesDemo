using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Mt.Tenants;

namespace Mt.Bank;

/// <summary>
/// The business code, written as for a single bank: not a single "where TenantId = …".
/// The tenant only comes into play to read ITS rules (limit, fees, account quota).
/// </summary>
public sealed class BankingService(BankDbContext db, ITenantContext context, TimeProvider clock)
{
    private Tenant Tenant => context.Require();
    private TenantRules Rules => Tenant.Rules;
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IReadOnlyList<AccountDto>> List() =>
        await db.Accounts.OrderBy(c => c.Owner).Select(c => new AccountDto(c.Id, c.Owner, c.Balance)).ToListAsync();

    public async Task<AccountDto?> Get(Guid id) =>
        await db.Accounts.Where(c => c.Id == id).Select(c => new AccountDto(c.Id, c.Owner, c.Balance)).FirstOrDefaultAsync();

    public async Task<AccountDto> Open(string owner, decimal initialDeposit)
    {
        if (string.IsNullOrWhiteSpace(owner) || initialDeposit < 0)
            throw new BusinessRuleException("Owner required and initial deposit positive or zero.");
        // DATA quota, specific to the customer's contract (the request quota is handled by the platform).
        if (await db.Accounts.CountAsync() >= Rules.MaxAccounts)
            throw new BusinessRuleException($"{Tenant.Name} has reached its quota of {Rules.MaxAccounts} accounts.");

        var account = new Account(Guid.NewGuid(), owner.Trim(), initialDeposit);
        db.Accounts.Add(account);
        db.Operations.Add(new Operation(account.Id, Now, "Initial deposit", initialDeposit));
        await db.SaveChangesAsync();
        return new AccountDto(account.Id, account.Owner, account.Balance);
    }

    public async Task<AccountDto> Withdraw(Guid id, decimal amount)
    {
        var account = await Load(id);
        account.Debit(amount);
        db.Operations.Add(new Operation(id, Now, "Withdrawal", -amount));
        await db.SaveChangesAsync();
        return new AccountDto(account.Id, account.Owner, account.Balance);
    }

    public async Task<TransferDto> Transfer(Guid sourceId, Guid targetId, decimal amount)
    {
        if (sourceId == targetId)
            throw new BusinessRuleException("The source and target accounts must be different.");
        // Same code for all the banks, with each one's parameters.
        if (amount > Rules.TransferLimit)
            throw new BusinessRuleException($"Transfer limit of {Tenant.Name}: {Rules.TransferLimit:0.00} €.");

        // The target is looked up at the current tenant only: a transfer to a customer of another bank
        // of the platform is impossible by construction (it would be an interbank transfer, another process).
        var source = await Load(sourceId);
        var target = await Load(targetId);
        var fee = Rules.TransferFee;

        source.Debit(amount + fee);
        target.Credit(amount);
        db.Operations.Add(new Operation(sourceId, Now, $"Transfer to {target.Owner}", -amount));
        db.Operations.Add(new Operation(targetId, Now, $"Transfer from {source.Owner}", amount));
        if (fee > 0)
            db.Operations.Add(new Operation(sourceId, Now, "Transfer fee", -fee));
        await db.SaveChangesAsync();
        return new TransferDto(sourceId, targetId, amount, fee, source.Balance);
    }

    public async Task<IReadOnlyList<OperationDto>> Operations(Guid id)
    {
        await Load(id);
        return await db.Operations.Where(o => o.AccountId == id).OrderBy(o => o.Date)
            .Select(o => new OperationDto(o.Date, o.Label, o.Amount)).ToListAsync();
    }

    /// <summary>
    /// Monthly account keeping fees, at THIS tenant's rate. Idempotent: replaying the same month does not debit twice.
    /// Called by a platform task, outside any HTTP request (see PlatformService).
    /// </summary>
    public async Task<int> ChargeMonthlyFees(string month)
    {
        if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new BusinessRuleException("Month expected in the YYYY-MM format.");
        var fee = Rules.MonthlyFee;
        if (fee <= 0)
            return 0;

        var key = $"fee-{month}";
        var alreadyCharged = await db.Operations.Where(o => o.Key == key).Select(o => o.AccountId).ToListAsync();
        var accounts = await db.Accounts.Where(c => !alreadyCharged.Contains(c.Id) && c.Balance > 0).ToListAsync();
        foreach (var account in accounts)
        {
            var amount = Math.Min(fee, account.Balance);
            account.Debit(amount);
            db.Operations.Add(new Operation(account.Id, Now, $"Account keeping fee {month}", -amount, key));
        }
        await db.SaveChangesAsync();
        return accounts.Count;
    }

    private async Task<Account> Load(Guid id) =>
        await db.Accounts.FirstOrDefaultAsync(c => c.Id == id) ?? throw new AccountNotFoundException(id);
}
