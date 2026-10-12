using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Es.Api;
using Es.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Es.Tests;

public class BankApplication : WebApplicationFactory<Program>
{
    // In-memory store: each test starts from a fresh history.
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Storage:File", "");
}

public class ApiTests
{
    private static readonly Guid Alice = DemoData.Alice;
    private static readonly Guid Bob = DemoData.Bob;

    private static async Task Withdraw(HttpClient client, decimal amount, string reason) =>
        (await client.PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount, reason })).EnsureSuccessStatusCode();

    private static async Task<JsonElement> WaitFor(HttpClient client, string url, Func<JsonElement, bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        JsonElement json;
        do
        {
            json = await client.GetFromJsonAsync<JsonElement>(url);
            if (condition(json)) return json;
            await Task.Delay(50);
        } while (DateTime.UtcNow < limit);
        Assert.Fail($"{url} did not reach the expected state: {json}");
        return json;
    }

    [Fact]
    public async Task The_state_of_an_account_can_be_seen_again_at_any_version()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        await Withdraw(client, 100m, "Groceries");
        await Withdraw(client, 200m, "Rent");

        var v2 = await client.GetFromJsonAsync<JsonElement>($"/accounts/{Alice}/state?version=2");
        var v3 = await client.GetFromJsonAsync<JsonElement>($"/accounts/{Alice}/state?version=3");

        Assert.Equal(900m, v2.GetProperty("balance").GetDecimal());
        Assert.Equal(700m, v3.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_change_based_on_a_stale_version_is_refused()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        await Withdraw(client, 100m, "Groceries");  // the account moves to version 2

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/accounts/{Alice}/withdrawal")
        {
            Content = JsonContent.Create(new { amount = 50m })
        };
        request.Headers.TryAddWithoutValidation("If-Match", "1");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>($"/accounts/{Alice}/events")).GetArrayLength());
    }

    [Fact]
    public async Task A_transfer_appends_an_event_to_each_of_the_two_streams()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        (await client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 250m })).EnsureSuccessStatusCode();

        var bob = await client.GetFromJsonAsync<JsonElement>($"/accounts/{Bob}/events");
        Assert.Equal(["AccountOpened", "TransferReceived"], bob.EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        await WaitFor(client, $"/accounts/{Bob}", r => r.GetProperty("balance").GetDecimal() == 750m);
    }

    [Fact]
    public async Task Rebuilt_projections_give_the_same_result()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        await Withdraw(client, 100m, "Groceries");
        await Withdraw(client, 300m, "Rent");
        var before = await WaitFor(client, $"/accounts/{Alice}", r => r.GetProperty("version").GetInt32() == 3);

        var rebuild = await client.PostAsync("/admin/projections/rebuild", null);

        var replayed = (await rebuild.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("eventsReplayed").GetInt32();
        Assert.Equal(4, replayed);  // 2 openings + 2 withdrawals
        Assert.Equal(before.ToString(), (await client.GetFromJsonAsync<JsonElement>($"/accounts/{Alice}")).ToString());
        var top = await client.GetFromJsonAsync<JsonElement>("/statistics/largest-withdrawals");
        Assert.Equal(["Rent", "Groceries"], top.EnumerateArray().Select(r => r.GetProperty("reason").GetString()));
    }

    [Fact]
    public void The_domain_depends_on_nothing()
    {
        var dependencies = typeof(Account).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Es."));
        Assert.Empty(dependencies);
    }
}
