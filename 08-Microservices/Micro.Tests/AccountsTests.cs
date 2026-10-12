using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Micro.Accounts;

namespace Micro.Tests;

public class AccountsTests
{
    private static readonly Guid Alice = AccountsDb.Alice;

    private static async Task<decimal> Balance(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/accounts/{id}")).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task A_debit_replayed_with_the_same_operationId_is_applied_only_once()
    {
        await using var service = new ServiceUnderTest<Marker>();
        var client = service.CreateClient();
        var debit = new { operationId = "transfer-42-debit", amount = 100m };

        var first = await client.PostAsJsonAsync($"/internal/accounts/{Alice}/debits", debit);
        var replayed = await client.PostAsJsonAsync($"/internal/accounts/{Alice}/debits", debit);  // network retry

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal(900m, await Balance(client, Alice));
    }

    [Fact]
    public async Task A_withdrawal_publishes_withdrawal_made_through_the_outbox()
    {
        await using var service = new ServiceUnderTest<Marker>();

        var response = await service.CreateClient().PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 100m });

        response.EnsureSuccessStatusCode();
        await Wait.Until(() => service.Broker.Calls.Any(a => a.Path == "/topics/withdrawal-made"));
        var message = service.Broker.Calls.Single(a => a.Path == "/topics/withdrawal-made");
        Assert.Contains("\"newBalance\":900", message.Body);
        Assert.NotNull(message.MessageId);
    }

    [Fact]
    public async Task If_the_broker_is_down_the_event_leaves_when_it_comes_back()
    {
        // The broker fails 3 times, then answers.
        var broker = new FakeHttp((_, number) => new HttpResponseMessage(
            number <= 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted));
        await using var service = new ServiceUnderTest<Marker>(new() { ["broker"] = broker });

        var response = await service.CreateClient().PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 100m });

        // The withdrawal succeeds right away, even if the broker is down…
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // …and the event, kept in the outbox, eventually leaves.
        await Wait.Until(() => broker.Calls.Count >= 4);
        Assert.Single(broker.Calls.Select(a => a.MessageId).Distinct());
    }
}
