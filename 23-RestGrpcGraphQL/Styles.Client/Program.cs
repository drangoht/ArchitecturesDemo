using System.Net;
using Styles.Client;
using Styles.Tooling.Grpc;

// The comparison client: the same uses, played with the server's three APIs (started separately).
//   dotnet run --project Styles.Client [-- --latency 50]   (simulated latency per request, in ms: a mobile network)

Console.OutputEncoding = System.Text.Encoding.UTF8;

var latency = TimeSpan.FromMilliseconds(args.SkipWhile(a => a != "--latency").Skip(1).Select(int.Parse).FirstOrDefault(50));

var restCounter = new HttpCounter { Latency = latency };
var graphQLCounter = new HttpCounter { Latency = latency };
var grpcCounter = new HttpCounter { Latency = latency };
var restUrl = new Uri("http://localhost:5123");
var clients = new (IBankClient Client, HttpCounter Counter)[]
{
    (new RestClient(new HttpClient(restCounter) { BaseAddress = restUrl }), restCounter),
    (new GraphQLClient(new HttpClient(graphQLCounter) { BaseAddress = restUrl }), graphQLCounter),
    (new GrpcBankClient(new HttpClient(grpcCounter) { BaseAddress = new Uri("http://localhost:5250") }), grpcCounter),
};

try
{
    await new HttpClient().GetAsync(restUrl);
}
catch (HttpRequestException)
{
    Console.WriteLine("The server does not answer: first start  dotnet run --project Styles.Server");
    return 1;
}

Console.WriteLine($"═══ 1. Alice's home screen (3 accounts, last 3 operations each) — simulated latency {latency.TotalMilliseconds} ms/request ═══\n");
foreach (var (client, _) in clients)
    await client.HomeScreen("bob");   // warm-up: connections open, code compiled, before measuring
HomeScreen? reference = null;
foreach (var (client, counter) in clients)
{
    var (screen, measure) = await Timing.Run(counter, () => client.HomeScreen("alice"));
    Console.WriteLine($"  {client.Name,-8} {measure}");
    reference ??= screen;
    if (!Identical(reference, screen))
        Console.WriteLine("           ⚠ screen different from the one obtained with REST!");
}
Console.WriteLine();
foreach (var account in reference!.Accounts)
{
    Console.WriteLine($"  {account.Label,-15} {account.Balance,10:N2} €");
    foreach (var operation in account.Operations)
        Console.WriteLine($"      {operation.Date:dd/MM}  {operation.Label,-22} {operation.Amount,10:N2} €");
}

Console.WriteLine("\n═══ 2. A refused transfer (insufficient balance): the same error, three languages ═══\n");
foreach (var (client, _) in clients)
{
    var result = await client.Transfer("20001", "10001", 1_000_000m, "Test");
    Console.WriteLine($"  {client.Name,-8} {result.Error,-22} \"{result.Message}\"");
}

Console.WriteLine("\n═══ 3. Following an account's operations live ═══\n");
var grpc = (GrpcBankClient)clients[2].Client;
var rest = new HttpClient(restCounter) { BaseAddress = restUrl };
using (var stopping = new CancellationTokenSource())
{
    var received = new List<string>();
    var listening = Task.Run(async () =>
    {
        try
        {
            // A single request, which stays open: the server pushes each operation. The deadline closes it.
            await foreach (var operation in grpc.FollowOperations("10001", TimeSpan.FromSeconds(2), stopping.Token))
                received.Add($"{operation.Label} ({operation.AmountCents / 100m:N2} €)");
        }
        catch (GrpcError e)
        {
            received.Add($"end of stream: {e.Status}");
        }
    });
    await Task.Delay(300);
    foreach (var (client, _) in clients)
        await client.Transfer("10002", "10001", 10m, $"Savings via {client.Name}");
    await listening;
    Console.WriteLine("  gRPC (server stream, 1 request open for 2 s):");
    foreach (var line in received)
        Console.WriteLine($"      {line}");
}

// REST has no stream: we poll. The ETag makes polling cheap when nothing changes.
string? etag = null;
int modified = 0, unchanged = 0;
var (_, pollingMeasure) = await Timing.Run(restCounter, async () =>
{
    for (var i = 0; i < 5; i++)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/accounts/10001");
        if (etag is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await rest.SendAsync(request);
        await response.Content.ReadAsByteArrayAsync();
        if (response.StatusCode == HttpStatusCode.NotModified)
            unchanged++;
        else
        {
            modified++;
            etag = response.Headers.ETag?.Tag;
        }
    }
    return 0;
});
Console.WriteLine($"\n  REST (polling with ETag, 5 times): {modified} × 200, {unchanged} × 304 Not Modified — {pollingMeasure}");

return 0;

static bool Identical(HomeScreen a, HomeScreen b) =>
    a.Name == b.Name && a.Accounts.Count == b.Accounts.Count && a.Accounts.Zip(b.Accounts).All(p =>
        p.First.Number == p.Second.Number && p.First.Label == p.Second.Label && p.First.Balance == p.Second.Balance
        && p.First.Operations.SequenceEqual(p.Second.Operations));
