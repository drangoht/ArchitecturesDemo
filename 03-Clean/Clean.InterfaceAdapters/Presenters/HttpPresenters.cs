using Clean.UseCases.Lookup;
using Clean.UseCases.Withdrawal;
using Clean.UseCases.Transfer;

namespace Clean.InterfaceAdapters.Presenters;

// JSON presenters: they implement the output boundaries and decide the shape of the response.

public class LookupHttpPresenter : ILookupOutput
{
    public HttpView View { get; private set; } = HttpView.Refused("No result.");

    public void Success(LookupResponse r) =>
        View = HttpView.Ok(new { id = r.AccountId, owner = r.Owner, balance = r.Balance });

    public void AccountNotFound(Guid accountId) => View = HttpView.NotFound(accountId);
}

public class WithdrawalHttpPresenter : IWithdrawalOutput
{
    public HttpView View { get; private set; } = HttpView.Refused("No result.");

    public void Success(WithdrawalResponse r) =>
        View = HttpView.Ok(new
        {
            balance = r.NewBalance,
            message = $"Withdrawal of {r.OldBalance - r.NewBalance:0.00} € done."
        });

    public void AccountNotFound(Guid accountId) => View = HttpView.NotFound(accountId);
    public void Refused(string reason) => View = HttpView.Refused(reason);
}

public class TransferHttpPresenter : ITransferOutput
{
    public HttpView View { get; private set; } = HttpView.Refused("No result.");

    public void Success(TransferResponse r) =>
        View = HttpView.Ok(new { amount = r.Amount, sourceBalance = r.SourceBalance, targetBalance = r.TargetBalance });

    public void AccountNotFound(Guid accountId) => View = HttpView.NotFound(accountId);
    public void Refused(string reason) => View = HttpView.Refused(reason);
}
