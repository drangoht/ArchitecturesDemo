using System.Net.Http.Json;
using System.Text.Json;
using Micro.Notifications;

namespace Micro.Tests;

public class NotificationsTests
{
    [Fact]
    public async Task A_message_received_twice_creates_only_one_notification()
    {
        await using var service = new ServiceUnderTest<Marker>();
        var client = service.CreateClient();
        var bob = Guid.NewGuid();
        var @event = new
        {
            transferId = Guid.NewGuid(), sourceId = Guid.NewGuid(), sourceOwner = "Alice",
            targetId = bob, targetOwner = "Bob", amount = 250m
        };
        var messageId = Guid.NewGuid().ToString();

        // The broker delivers "at least once": a double delivery of the same message is simulated.
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/events/transfer-made")
            {
                Content = JsonContent.Create(@event)
            };
            request.Headers.Add("Message-Id", messageId);
            (await client.SendAsync(request)).EnsureSuccessStatusCode();
        }

        var notifications = await client.GetFromJsonAsync<JsonElement>($"/notifications/{bob}");
        Assert.Contains("received from Alice", Assert.Single(notifications.EnumerateArray()).GetProperty("message").GetString());
    }
}
