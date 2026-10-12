using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Micro.Broker;

namespace Micro.Tests;

public class BrokerTests
{
    [Fact]
    public async Task A_message_is_redelivered_until_the_subscriber_answers()
    {
        // The subscriber is down for the first 2 deliveries.
        var subscriber = new FakeHttp((_, number) => new HttpResponseMessage(
            number <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        await using var service = new ServiceUnderTest<Marker>(
            new() { ["subscribers"] = subscriber },
            new() { ["Subscriptions:test:0"] = "http://subscriber.local/receive" });
        var client = service.CreateClient();

        var publication = await client.PostAsync("/topics/test", JsonContent.Create(new { hello = "world" }));

        Assert.Equal(HttpStatusCode.Accepted, publication.StatusCode);
        await Wait.Until(() => subscriber.Calls.Count >= 3);
        var state = await client.GetFromJsonAsync<JsonElement>("/state");
        Assert.Equal(1, state.GetProperty("delivered").GetInt32());
        Assert.Equal(0, state.GetProperty("pending").GetArrayLength());
        Assert.All(subscriber.Calls, a => Assert.Equal("{\"hello\":\"world\"}", a.Body));
    }
}
