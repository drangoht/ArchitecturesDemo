using System.Text.Json;
using Sls.Functions;
using Sls.Platform;

namespace Sls.Tests;

/// <summary>A serverless function is a mere method: it is called directly, with in-memory managed services.</summary>
public class FunctionsTests
{
    private readonly ManagedServices _services = new(new PlatformOptions());
    private FunctionContext Context => FunctionContext.ForTests(_services);

    public FunctionsTests() => DemoData.Load(_services);

    private static HttpRequestData Request(object body, string id = "") =>
        new("POST", "/", new Dictionary<string, string> { ["id"] = id }, JsonSerializer.Serialize(body));

    private async Task<decimal> Balance(Guid id) => (await _services.Table(Tables.Accounts).Read<Account>(id.ToString())).Value!.Balance;

    [Fact]
    public async Task Simultaneous_withdrawals_never_overdraw_the_account()
    {
        // 30 "instances" withdraw 100 € at the same time from an account of 1,000 €: thanks to the ETags, exactly 10 succeed.
        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
            AccountsFunctions.Withdraw(Request(new { amount = 100m }, DemoData.Alice.ToString()), Context)));

        Assert.Equal(10, responses.Count(r => r.Status == 200));
        Assert.Equal(0m, await Balance(DemoData.Alice));
    }

    [Fact]
    public async Task A_transfer_message_delivered_twice_debits_only_once()
    {
        var transfer = await TransfersFunctions.Schedule(
            new TransfersFunctions.TransferRequest(DemoData.Alice, DemoData.Bob, 100m), Context, Guid.NewGuid());
        var message = new QueueMessage("m1", Queues.Transfers, JsonSerializer.Serialize(new { transferId = transfer.Id }), 1);

        await TransfersFunctions.Execute(message, Context);
        await TransfersFunctions.Execute(message, Context);  // double delivery

        Assert.Equal(900m, await Balance(DemoData.Alice));
        Assert.Equal(600m, await Balance(DemoData.Bob));
    }

    [Fact]
    public async Task A_business_refusal_is_final_and_triggers_no_retry()
    {
        var transfer = await TransfersFunctions.Schedule(
            new TransfersFunctions.TransferRequest(DemoData.Bob, DemoData.Alice, 9_999m), Context, Guid.NewGuid());

        // No exception: the platform considers the message as processed.
        await TransfersFunctions.Execute(
            new QueueMessage("m1", Queues.Transfers, JsonSerializer.Serialize(new { transferId = transfer.Id }), 1), Context);

        var (result, _) = await _services.Table(Tables.Transfers).Read<Transfer>(transfer.Id.ToString());
        Assert.Equal(("Refused", "Insufficient balance."), (result!.Status, result.Error));
    }

    [Fact]
    public async Task Reprocessing_the_same_import_file_creates_no_duplicates()
    {
        var file = new StoredObject("imports/test.csv",
            $"source;target;amount\n{DemoData.Carla};{DemoData.Alice};10\n{DemoData.Carla};{DemoData.Bob};20\n");

        await ImportsFunctions.Process(file, Context);
        await ImportsFunctions.Process(file, Context);  // retry of the storage trigger

        Assert.Equal(2, (await _services.Table(Tables.Transfers).List<Transfer>()).Count);
    }

    [Fact]
    public async Task The_instance_memory_is_not_shared_unlike_the_managed_service()
    {
        var (instanceA, instanceB) = (new Dictionary<string, object>(), new Dictionary<string, object>());
        var request = new HttpRequestData("GET", "/visits", new Dictionary<string, string>(), "");

        await VisitsFunctions.Count(request, FunctionContext.ForTests(_services, instanceA));
        await VisitsFunctions.Count(request, FunctionContext.ForTests(_services, instanceA));
        var response = await VisitsFunctions.Count(request, FunctionContext.ForTests(_services, instanceB));

        var body = JsonSerializer.SerializeToElement(response.Body);
        Assert.Equal(1, body.GetProperty("instanceCounter").GetInt64());  // instance B knows nothing of the first 2 calls
        Assert.Equal(3, body.GetProperty("globalCounter").GetInt64());
    }
}
