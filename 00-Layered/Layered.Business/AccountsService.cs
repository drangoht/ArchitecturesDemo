using Layered.Data;

namespace Layered.Business;

/// <summary>
/// Business service: all the rules are here and apply to the entities of the data layer.
/// "Transaction Script" style: one method = one use case, from start to finish.
/// </summary>
public class AccountsService(IAccountsDao dao)
{
    public const decimal TransferLimit = 5_000m;

    public async Task<AccountDto> Get(Guid id) => ToDto(await Load(id));

    public async Task<AccountDto> Withdraw(Guid id, decimal amount)
    {
        var account = await Load(id);
        CheckWithdrawalAllowed(account, amount);

        account.Balance -= amount;
        await dao.Save();
        return ToDto(account);
    }

    public async Task Transfer(Guid sourceId, Guid targetId, decimal amount)
    {
        if (sourceId == targetId)
            throw new BusinessRuleException("Cannot transfer to the same account.");
        if (amount > TransferLimit)
            throw new BusinessRuleException($"A transfer cannot exceed {TransferLimit:0} €.");

        var source = await Load(sourceId);
        var target = await Load(targetId);
        CheckWithdrawalAllowed(source, amount);

        source.Balance -= amount;
        target.Balance += amount;
        await dao.Save();
    }

    // Since the entity is anemic, nothing stops another class from doing "account.Balance -= x"
    // without going through this check: the rule is protected by discipline alone.
    private static void CheckWithdrawalAllowed(AccountEntity account, decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be strictly positive.");
        if (amount > account.Balance)
            throw new BusinessRuleException("Insufficient balance.");
    }

    private async Task<AccountEntity> Load(Guid id) =>
        await dao.ById(id) ?? throw new AccountNotFoundException(id);

    private static AccountDto ToDto(AccountEntity account) => new(account.Id, account.Owner, account.Balance);
}
