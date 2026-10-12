using Styles.Bank;
using Styles.Tooling.Grpc;
using P = Styles.Contracts;

namespace Styles.Server;

/// <summary>
/// The gRPC API: PROCEDURES (verbs: GetDashboard, MakeTransfer…), described in bank.proto, and
/// compact binary messages. The contract is strict and shared: client and server are generated from the same
/// file. Each method is tailored for a use (the home screen in one call), and the server can PUSH
/// messages (stream). Ideal between services; a browser, however, cannot call it directly.
/// </summary>
public static class GrpcApi
{
    public static void MapGrpc(this IEndpointRouteBuilder app)
    {
        var bank = app.ServiceProvider.GetRequiredService<BankService>();
        const string service = P.BankGrpcService.Name;

        app.MapGrpcUnary<P.DashboardRequest, P.Dashboard>(service, "GetDashboard", (request, _) => Translate(() =>
        {
            var customer = bank.Customer(request.CustomerId);
            var accounts = bank.AccountsOf(customer.Id);
            var count = request.OperationsPerAccount == 0 ? 3 : request.OperationsPerAccount;   // 0 = not provided (proto3)
            var operations = bank.LatestOperations(accounts.Select(c => c.Number).ToList(), count);   // a single read
            var dashboard = new P.Dashboard { CustomerId = customer.Id, Name = customer.Name };
            dashboard.Accounts.AddRange(accounts.Select(c => Message(c, operations[c.Number])));
            return dashboard;
        }));

        app.MapGrpcUnary<P.AccountRequest, P.Account>(service, "GetAccount", (request, _) =>
            Translate(() => Message(bank.Account(request.Number), [])));

        app.MapGrpcUnary<P.TransferRequest, P.Transfer>(service, "MakeTransfer", (request, _) => Translate(() =>
        {
            var v = bank.MakeTransfer(request.Source, request.Target, request.AmountCents / 100m, request.Label);
            return new P.Transfer
            {
                Id = v.Id, Source = v.Source, Target = v.Target,
                AmountCents = Cents(v.Amount), SourceBalanceCents = Cents(v.SourceBalance)
            };
        }));

        // The server stream: the HTTP/2 response stays open, and each operation leaves in it as soon as it happens.
        app.MapGrpcServerStream<P.FollowRequest, P.Operation>(service, "FollowOperations", async (request, write, cancellation) =>
        {
            var operations = await Translate(() => bank.Follow(request.Number, cancellation));
            await foreach (var operation in operations.ReadAllAsync(cancellation))
                await write(Message(operation));
        });
    }

    private static Task<T> Translate<T>(Func<T> handler)
    {
        try
        {
            return Task.FromResult(handler());
        }
        catch (BusinessError e)
        {
            throw new GrpcError(Translations.GrpcStatus(e.Code), e.Message);
        }
    }

    private static long Cents(decimal euros) => (long)(euros * 100);

    private static P.Account Message(Account account, IEnumerable<Operation> operations)
    {
        var message = new P.Account
        {
            Number = account.Number,
            Label = account.Label,
            Type = account.Type == AccountType.Current ? P.AccountType.Current : P.AccountType.Savings,
            BalanceCents = Cents(account.Balance)
        };
        message.LatestOperations.AddRange(operations.Select(Message));
        return message;
    }

    private static P.Operation Message(Operation operation) => new()
    {
        Id = operation.Id,
        DateUnixMs = operation.Date.ToUnixTimeMilliseconds(),
        Label = operation.Label,
        AmountCents = Cents(operation.Amount)
    };
}
