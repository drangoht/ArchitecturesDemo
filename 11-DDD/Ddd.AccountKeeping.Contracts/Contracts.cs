namespace Ddd.AccountKeeping.Contracts;

/// <summary>Integration event published on each transfer (the amount in euros is provided at Compliance's request).</summary>
public record TransferSent(
    Guid TransferId,
    Guid SourceAccountId,
    string SourceIban,
    string SourceOwner,
    string BeneficiaryIban,
    decimal Amount,
    string Currency,
    decimal AmountInEuros,
    DateTime Date);

/// <summary>Implemented by the contexts that want to be told what happens in Account keeping.</summary>
public interface IAccountKeepingSubscriber
{
    Task OnTransferSent(TransferSent @event);
}

/// <summary>
/// Open Host Service: what Account keeping agrees to do for the other contexts.
/// </summary>
public interface IAccountKeepingApi
{
    Task BlockAccount(Guid accountId, string reason);
}
