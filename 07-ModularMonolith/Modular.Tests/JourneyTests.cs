using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Modular.Accounts;

namespace Modular.Tests;

// End-to-end journeys across several modules.
public class JourneyTests
{
    private static readonly Guid Alice = AccountsModule.Alice;
    private static readonly Guid Bob = AccountsModule.Bob;

    private static async Task<string[]> Notifications(HttpClient client, Guid accountId)
    {
        var json = await client.GetFromJsonAsync<JsonElement>($"/notifications/{accountId}");
        return json.EnumerateArray().Select(n => n.GetProperty("message").GetString()!).ToArray();
    }

    [Fact]
    public async Task A_transfer_goes_through_the_three_modules()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 250m });

        response.EnsureSuccessStatusCode();
        // Accounts module: the balances moved (synchronous call from Transfers).
        var bob = await client.GetFromJsonAsync<JsonElement>($"/accounts/{Bob}");
        Assert.Equal(750m, bob.GetProperty("balance").GetDecimal());
        // Transfers module: history in its own database.
        var history = await client.GetFromJsonAsync<JsonElement>($"/transfers/account/{Bob}");
        Assert.Equal(1, history.GetArrayLength());
        // Notifications module: fed only by the TransferMade event.
        Assert.Contains("received from Alice", Assert.Single(await Notifications(client, Bob)));
        Assert.Contains("sent to Bob", Assert.Single(await Notifications(client, Alice)));
    }

    [Fact]
    public async Task A_withdrawal_publishes_an_event_that_creates_a_notification()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        await client.PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 100m });

        Assert.StartsWith("Withdrawal of 100", Assert.Single(await Notifications(client, Alice)));
    }

    [Fact]
    public async Task A_refused_transfer_publishes_nothing()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers", new { source = Bob, target = Alice, amount = 99_999m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await Notifications(client, Bob));
    }
}
