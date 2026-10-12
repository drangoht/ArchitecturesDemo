using Clean.UseCases.Lookup;
using Clean.UseCases.Withdrawal;
using Clean.UseCases.Transfer;

namespace Clean.InterfaceAdapters.Controllers;

/// <summary>
/// Controller: converts a raw input (route parameters, JSON body…) into a request model
/// and calls the input boundary. It never reads the result: that is the presenter's job.
/// </summary>
public class AccountsController(ILookupInput lookup, IWithdrawalInput withdrawal, ITransferInput transfer)
{
    public Task Get(Guid accountId, ILookupOutput presenter) =>
        lookup.Execute(new LookupRequest(accountId), presenter);

    public Task Withdraw(Guid accountId, decimal amount, IWithdrawalOutput presenter) =>
        withdrawal.Execute(new WithdrawalRequest(accountId, amount), presenter);

    public Task Transfer(Guid sourceId, Guid targetId, decimal amount, ITransferOutput presenter) =>
        transfer.Execute(new TransferRequest(sourceId, targetId, amount), presenter);
}
