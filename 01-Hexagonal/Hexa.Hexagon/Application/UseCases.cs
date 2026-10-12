using Hexa.Hexagon.Domain;
using Hexa.Hexagon.Ports.Inbound;
using Hexa.Hexagon.Ports.Outbound;

namespace Hexa.Hexagon.Application;

// Each use case implements an inbound port, relying only on outbound ports.

public class GetAccount(IAccounts accounts) : IGetAccount
{
    public async Task<AccountView> Execute(Guid accountId)
    {
        var account = await accounts.Load(accountId) ?? throw new AccountNotFoundException(accountId);
        return new AccountView(account.Id, account.Owner, account.Balance);
    }
}

public class WithdrawMoney(IAccounts accounts, INotifications notifications) : IWithdrawMoney
{
    public async Task Execute(Guid accountId, decimal amount)
    {
        var account = await accounts.Load(accountId) ?? throw new AccountNotFoundException(accountId);
        account.Withdraw(amount);
        await accounts.Save(account);
        await notifications.WithdrawalMade(accountId, amount);
    }
}

public class MakeTransfer(IAccounts accounts, INotifications notifications) : IMakeTransfer
{
    public async Task Execute(Guid sourceId, Guid targetId, decimal amount)
    {
        if (sourceId == targetId)
            throw new BusinessRuleException("Cannot transfer to the same account.");

        var source = await accounts.Load(sourceId) ?? throw new AccountNotFoundException(sourceId);
        var target = await accounts.Load(targetId) ?? throw new AccountNotFoundException(targetId);

        source.Withdraw(amount);
        target.Deposit(amount);

        await accounts.Save(source);
        await accounts.Save(target);
        await notifications.TransferMade(sourceId, targetId, amount);
    }
}
