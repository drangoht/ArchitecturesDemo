using Clean.UseCases.Gateways;

namespace Clean.UseCases.Lookup;

// Request model / Response model: simple data structures, with no dependency on the web.
public record LookupRequest(Guid AccountId);
public record LookupResponse(Guid AccountId, string Owner, decimal Balance);

// Input boundary: what the controller calls.
public interface ILookupInput
{
    Task Execute(LookupRequest request, ILookupOutput output);
}

// Output boundary: what the interactor calls to deliver its result (implemented by a presenter).
public interface ILookupOutput
{
    void Success(LookupResponse response);
    void AccountNotFound(Guid accountId);
}

// Interactor: the use case itself.
public class LookupInteractor(IAccountsGateway accounts) : ILookupInput
{
    public async Task Execute(LookupRequest request, ILookupOutput output)
    {
        var account = await accounts.Load(request.AccountId);
        if (account is null)
        {
            output.AccountNotFound(request.AccountId);
            return;
        }

        output.Success(new LookupResponse(account.Id, account.Owner, account.Balance));
    }
}
