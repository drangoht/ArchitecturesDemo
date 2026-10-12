using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Styles.Tooling.Grpc;
using P = Styles.Contracts;

namespace Styles.Client;

// What the mobile app's home screen displays: the customer's name, and for each account its label, its
// balance and its last three operations. Nothing more.
public sealed record OperationLine(DateTimeOffset Date, string Label, decimal Amount);
public sealed record AccountLine(string Number, string Label, decimal Balance, IReadOnlyList<OperationLine> Operations);
public sealed record HomeScreen(string Name, IReadOnlyList<AccountLine> Accounts);

/// <summary>The result of a transfer; on failure, the error as each protocol gives it.</summary>
public sealed record TransferResult(string? Id, decimal? SourceBalance, string? Error, string? Message);

/// <summary>The same app, three ways of talking to the server.</summary>
public interface IBankClient
{
    string Name { get; }
    Task<HomeScreen> HomeScreen(string customerId, int operationsPerAccount = 3);
    Task<TransferResult> Transfer(string source, string target, decimal amount, string label);
}

/// <summary>REST: we follow the resources. The customer, then their accounts, then the operations of EACH account.</summary>
public sealed class RestClient(HttpClient http) : IBankClient
{
    public string Name => "REST";

    public async Task<HomeScreen> HomeScreen(string customerId, int operationsPerAccount = 3)
    {
        var customer = await http.GetFromJsonAsync<JsonObject>($"/customers/{customerId}");
        var accounts = await http.GetFromJsonAsync<JsonArray>($"/customers/{customerId}/accounts");
        // 1 + 1 + N: one request per account for its operations (in parallel, but one request each).
        var lines = await Task.WhenAll(accounts!.Select(async c =>
        {
            var number = c!["number"]!.GetValue<string>();
            var operations = await http.GetFromJsonAsync<JsonArray>($"/accounts/{number}/operations?limit={operationsPerAccount}");
            return new AccountLine(number, c["label"]!.GetValue<string>(), c["balance"]!.GetValue<decimal>(),
                operations!.Select(o => new OperationLine(o!["date"]!.GetValue<DateTimeOffset>(), o["label"]!.GetValue<string>(),
                    o["amount"]!.GetValue<decimal>())).ToList());
        }));
        return new HomeScreen(customer!["name"]!.GetValue<string>(), lines);
    }

    public async Task<TransferResult> Transfer(string source, string target, decimal amount, string label)
    {
        var response = await http.PostAsJsonAsync("/transfers", new { source, target, amount, label });
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        if (response.StatusCode == HttpStatusCode.Created)
            return new TransferResult(body!["id"]!.GetValue<string>(), body["sourceBalance"]!.GetValue<decimal>(), null, null);
        // The HTTP status gives the category, the "problem+json" body the detail.
        return new TransferResult(null, null, $"{(int)response.StatusCode} {body!["code"]!.GetValue<string>()}", body["title"]?.GetValue<string>());
    }
}

/// <summary>GraphQL: a query that describes the whole screen, and the response has exactly its shape.</summary>
public sealed class GraphQLClient(HttpClient http) : IBankClient
{
    public const string HomeScreenQuery = """
        query HomeScreen($id: ID!, $n: Int) {
          customer(id: $id) {
            name
            accounts { number label balance operations(latest: $n) { date label amount } }
          }
        }
        """;

    public const string TransferMutation = """
        mutation Transfer($source: ID!, $target: ID!, $amount: Float!, $label: String) {
          makeTransfer(source: $source, target: $target, amount: $amount, label: $label) {
            id
            source { balance }
          }
        }
        """;

    public string Name => "GraphQL";

    public async Task<JsonObject> Execute(string query, object? variables = null)
    {
        var response = await http.PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();   // 200 even for a business error: it is the body that counts
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    public async Task<HomeScreen> HomeScreen(string customerId, int operationsPerAccount = 3)
    {
        var response = await Execute(HomeScreenQuery, new { id = customerId, n = operationsPerAccount });
        var customer = response["data"]?["customer"] ?? throw new InvalidOperationException(response["errors"]?.ToJsonString());
        return new HomeScreen(customer["name"]!.GetValue<string>(), customer["accounts"]!.AsArray().Select(c => new AccountLine(
            c!["number"]!.GetValue<string>(), c["label"]!.GetValue<string>(), (decimal)c["balance"]!.GetValue<double>(),
            c["operations"]!.AsArray().Select(o => new OperationLine(DateTimeOffset.Parse(o!["date"]!.GetValue<string>()),
                o["label"]!.GetValue<string>(), (decimal)o["amount"]!.GetValue<double>())).ToList())).ToList());
    }

    public async Task<TransferResult> Transfer(string source, string target, decimal amount, string label)
    {
        var response = await Execute(TransferMutation, new { source, target, amount, label });
        if (response["errors"] is JsonArray errors)
            return new TransferResult(null, null, errors[0]!["extensions"]!["code"]!.GetValue<string>(), errors[0]!["message"]!.GetValue<string>());
        var transfer = response["data"]!["makeTransfer"]!;
        return new TransferResult(transfer["id"]!.GetValue<string>(), (decimal)transfer["source"]!["balance"]!.GetValue<double>(), null, null);
    }
}

/// <summary>gRPC: a procedure tailored to the screen, binary messages, a typed client.</summary>
public sealed class GrpcBankClient(HttpClient http) : IBankClient
{
    private readonly GrpcClient _grpc = new(http, P.BankGrpcService.Name);

    public string Name => "gRPC";

    public GrpcClient Grpc => _grpc;

    public async Task<HomeScreen> HomeScreen(string customerId, int operationsPerAccount = 3)
    {
        var dashboard = await _grpc.Unary<P.DashboardRequest, P.Dashboard>("GetDashboard",
            new P.DashboardRequest { CustomerId = customerId, OperationsPerAccount = operationsPerAccount });
        return new HomeScreen(dashboard.Name, dashboard.Accounts.Select(c => new AccountLine(c.Number, c.Label, c.BalanceCents / 100m,
            c.LatestOperations.Select(o => new OperationLine(DateTimeOffset.FromUnixTimeMilliseconds(o.DateUnixMs), o.Label,
                o.AmountCents / 100m)).ToList())).ToList());
    }

    public async Task<TransferResult> Transfer(string source, string target, decimal amount, string label)
    {
        try
        {
            var transfer = await _grpc.Unary<P.TransferRequest, P.Transfer>("MakeTransfer", new P.TransferRequest
            {
                Source = source, Target = target, AmountCents = (long)(amount * 100), Label = label
            });
            return new TransferResult(transfer.Id, transfer.SourceBalanceCents / 100m, null, null);
        }
        catch (GrpcError e)   // one of the 17 gRPC statuses: the category, not the business code
        {
            return new TransferResult(null, null, e.Status.ToString(), e.Message);
        }
    }

    public IAsyncEnumerable<P.Operation> FollowOperations(string number, TimeSpan? deadline = null, CancellationToken cancellation = default) =>
        _grpc.Stream<P.FollowRequest, P.Operation>("FollowOperations", new P.FollowRequest { Number = number }, deadline, cancellation);
}
