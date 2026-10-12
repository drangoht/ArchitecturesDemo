using Clean.UseCases.Gateways;
using Clean.Entities;

namespace Clean.UseCases.Transfer;

public record TransferRequest(Guid SourceId, Guid TargetId, decimal Amount);
public record TransferResponse(decimal Amount, decimal SourceBalance, decimal TargetBalance);

public interface ITransferInput
{
    Task Execute(TransferRequest request, ITransferOutput output);
}

public interface ITransferOutput
{
    void Success(TransferResponse response);
    void AccountNotFound(Guid accountId);
    void Refused(string reason);
}

public class TransferInteractor(IAccountsGateway accounts) : ITransferInput
{
    public async Task Execute(TransferRequest request, ITransferOutput output)
    {
        if (request.SourceId == request.TargetId)
        {
            output.Refused("Cannot transfer to the same account.");
            return;
        }

        var source = await accounts.Load(request.SourceId);
        if (source is null)
        {
            output.AccountNotFound(request.SourceId);
            return;
        }

        var target = await accounts.Load(request.TargetId);
        if (target is null)
        {
            output.AccountNotFound(request.TargetId);
            return;
        }

        try
        {
            source.Withdraw(request.Amount);
            target.Deposit(request.Amount);
        }
        catch (BusinessRuleException e)
        {
            output.Refused(e.Message);
            return;
        }

        await accounts.Save(source);
        await accounts.Save(target);
        output.Success(new TransferResponse(request.Amount, source.Balance, target.Balance));
    }
}
