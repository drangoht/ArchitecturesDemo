using Microsoft.Extensions.DependencyInjection.Extensions;
using Styles.Bank;
using Styles.Server;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BankService>();
builder.Services.AddSingleton(s => GraphQLApi.Engine(s.GetRequiredService<BankService>()));

var app = builder.Build();

app.MapRest();      // /customers, /accounts, /transfers
app.MapGraphQL();   // /graphql
app.MapGrpc();      // /bank.Bank/…

app.MapGet("/", () => Results.Text("""
    The same bank, three APIs:
      REST     http://localhost:5123/customers/alice, /customers/alice/accounts, /accounts/10001, /accounts/10001/operations, POST /transfers
      GraphQL  POST http://localhost:5123/graphql; schema: GET /graphql/schema; fields used: GET /graphql/usage
      gRPC     http://localhost:5250 (HTTP/2), service bank.Bank, contract: Styles.Contracts/bank.proto
    The comparison client: dotnet run --project Styles.Client
    """, "text/plain; charset=utf-8"));

app.MapGet("/statistics", (BankService bank) => Results.Ok(new
{
    operationReads = bank.Statistics.OperationReads,
    streamSubscribers = bank.SubscriberCount
}));

app.Run();

namespace Styles.Server
{
    /// <summary>Assembly marker, for the tests (the client also has a Program).</summary>
    public sealed class Marker;
}
