using Ui.Bank;

namespace Mvu.Bank;

/// <summary>
/// The only code that calls the bank: it performs the effects asked for by Update and turns each result,
/// success or business error, into a message. Swap it for a fake one and the whole screen runs without a bank.
/// </summary>
public sealed class BankEffects(IBank bank)
{
    public async Task<Msg> Perform(Effect effect, CancellationToken ct)
    {
        switch (effect)
        {
            case LoadAccounts:
                return new AccountsLoaded(await bank.AccountsAsync(ct));
            case LoadOperations load:
                try
                {
                    return new OperationsLoaded(load.Request, await bank.OperationsAsync(load.Account, ct));
                }
                catch (BusinessError error)
                {
                    return new OperationsFailed(load.Request, error.Message);
                }
            case SendTransfer send:
                try
                {
                    return new TransferDone(await bank.TransferAsync(send.Source, send.Target, send.Amount, ct));
                }
                catch (BusinessError error)
                {
                    // The bank is the authority: between the input and the sending, the balance may have changed.
                    return new TransferFailed(error.Message);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, "Unknown effect.");
        }
    }
}
