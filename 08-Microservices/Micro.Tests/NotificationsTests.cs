using System.Net.Http.Json;
using System.Text.Json;
using Micro.Notifications;

namespace Micro.Tests;

public class NotificationsTests
{
    [Fact]
    public async Task Un_message_recu_deux_fois_ne_cree_qu_une_notification()
    {
        await using var service = new ServiceEnTest<Marqueur>();
        var client = service.CreateClient();
        var bob = Guid.NewGuid();
        var evenement = new
        {
            virementId = Guid.NewGuid(), sourceId = Guid.NewGuid(), titulaireSource = "Alice",
            cibleId = bob, titulaireCible = "Bob", montant = 250m
        };
        var messageId = Guid.NewGuid().ToString();

        // Le courtier livre « au moins une fois » : on simule une double livraison du même message.
        for (var i = 0; i < 2; i++)
        {
            using var requete = new HttpRequestMessage(HttpMethod.Post, "/evenements/virement-effectue")
            {
                Content = JsonContent.Create(evenement)
            };
            requete.Headers.Add("Message-Id", messageId);
            (await client.SendAsync(requete)).EnsureSuccessStatusCode();
        }

        var notifications = await client.GetFromJsonAsync<JsonElement>($"/notifications/{bob}");
        Assert.Contains("reçu de Alice", Assert.Single(notifications.EnumerateArray()).GetProperty("message").GetString());
    }
}
