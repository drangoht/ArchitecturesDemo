using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Micro.Transfers;

namespace Micro.Tests;

/// <summary>The Transfers service is tested alone: the Accounts service is a fake HTTP server.</summary>
public class TransfersTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    /// <summary>Simulates the Accounts service. The response of the debits and credits is chosen (call number as parameter).</summary>
    private static FakeHttp FakeAccounts(
        Func<int, HttpResponseMessage>? debits = null,
        Func<Call, HttpResponseMessage>? credits = null)
    {
        var debitNumber = 0;
        return new FakeHttp((call, _) =>
        {
            if (call.Method == HttpMethod.Get)
            {
                var id = Guid.Parse(call.Path.Split('/').Last());
                return FakeHttp.Json(HttpStatusCode.OK, new { id, owner = id == Alice ? "Alice" : "Bob", balance = 1000m });
            }
            if (call.Path.EndsWith("/debits"))
                return debits?.Invoke(Interlocked.Increment(ref debitNumber)) ?? new HttpResponseMessage(HttpStatusCode.OK);
            return credits?.Invoke(call) ?? new HttpResponseMessage(HttpStatusCode.OK);
        });
    }

    private static async Task<(HttpStatusCode Code, string Status)> Transfer(ServiceUnderTest<Marker> service)
    {
        var response = await service.CreateClient().PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 50m });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, body.GetProperty("status").GetString()!);
    }

    [Fact]
    public async Task A_successful_transfer_publishes_transfer_made()
    {
        await using var service = new ServiceUnderTest<Marker>(new() { ["accounts"] = FakeAccounts() });

        var (code, status) = await Transfer(service);

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal("Done", status);
        await Wait.Until(() => service.Broker.Calls.Any(a => a.Path == "/topics/transfer-made"));
    }

    [Fact]
    public async Task A_transient_failure_is_absorbed_by_the_retry_without_double_debit()
    {
        // The 1st debit call fails (503), the automatic retry succeeds.
        var accounts = FakeAccounts(debits: number => new HttpResponseMessage(
            number == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        await using var service = new ServiceUnderTest<Marker>(new() { ["accounts"] = accounts });

        var (_, status) = await Transfer(service);

        Assert.Equal("Done", status);
        // Two debit calls… with the SAME operationId: Accounts will debit only once.
        var debits = accounts.Calls.Where(a => a.Path.EndsWith("/debits")).ToList();
        Assert.Equal(2, debits.Count);
        Assert.Single(debits.Select(a => JsonDocument.Parse(a.Body!).RootElement.GetProperty("operationId").GetString()).Distinct());
    }

    [Fact]
    public async Task If_the_credit_is_refused_the_debit_is_compensated()
    {
        var accounts = FakeAccounts(credits: call => call.Path.Contains(Bob.ToString())
            ? FakeHttp.Json(HttpStatusCode.BadRequest, new { error = "Account blocked." })
            : new HttpResponseMessage(HttpStatusCode.OK));
        await using var service = new ServiceUnderTest<Marker>(new() { ["accounts"] = accounts });

        var (code, status) = await Transfer(service);

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Equal("Cancelled", status);
        var compensation = Assert.Single(accounts.Calls, a => a.Path == $"/internal/accounts/{Alice}/credits");
        Assert.Contains("-compensation", compensation.Body);
        Assert.DoesNotContain(service.Broker.Calls, a => a.Path == "/topics/transfer-made");
    }

    [Fact]
    public async Task If_Accounts_is_unreachable_the_transfer_is_marked_to_resume()
    {
        var unreachable = new FakeHttp((_, _) => throw new HttpRequestException("Connection refused"));
        await using var service = new ServiceUnderTest<Marker>(new() { ["accounts"] = unreachable });

        var (code, status) = await Transfer(service);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, code);
        Assert.Equal("ToResume", status);
        Assert.Equal(4, unreachable.Calls.Count);  // 1 call + 3 retries, then give up
    }
}
