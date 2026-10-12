using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cqrs.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cqrs.Tests;

public class BankApplication(int projectionDelayMs) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseName", Guid.NewGuid().ToString());
        builder.UseSetting("Projections:DelayMs", projectionDelayMs.ToString());
    }
}

public class ApiTests
{
    private static readonly Guid Alice = DemoData.Alice;
    private static readonly Guid Bob = DemoData.Bob;

    private static async Task<decimal?> Balance(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/accounts/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
    }

    private static async Task WaitForBalance(HttpClient client, Guid id, decimal expected)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (await Balance(client, id) != expected && DateTime.UtcNow < limit)
            await Task.Delay(50);
        Assert.Equal(expected, await Balance(client, id));
    }

    [Fact]
    public async Task A_command_is_accepted_then_visible_in_the_views()
    {
        await using var app = new BankApplication(projectionDelayMs: 0);
        var client = app.CreateClient();
        await WaitForBalance(client, Bob, 500m);

        var response = await client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 250m });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitForBalance(client, Bob, 750m);
        var operations = await client.GetFromJsonAsync<JsonElement>($"/accounts/{Bob}/operations");
        Assert.Equal("Transfer received from Alice", operations[0].GetProperty("label").GetString());
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/dashboard");
        Assert.Equal(1, dashboard.GetProperty("transferCount").GetInt32());
    }

    [Fact]
    public async Task The_read_side_is_eventually_consistent()
    {
        await using var app = new BankApplication(projectionDelayMs: 1500);
        var client = app.CreateClient();
        await WaitForBalance(client, Alice, 1000m);

        await client.PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 100m });

        // Right after the command, the view is not up to date yet…
        Assert.Equal(1000m, await Balance(client, Alice));
        var state = await client.GetFromJsonAsync<JsonElement>("/admin/projections");
        Assert.Equal(1, state.GetProperty("lag").GetInt64());
        // …it becomes so as soon as the projection has run.
        await WaitForBalance(client, Alice, 900m);
    }

    [Fact]
    public async Task An_invalid_command_returns_all_the_validation_errors()
    {
        await using var app = new BankApplication(projectionDelayMs: 0);

        var response = await app.CreateClient().PostAsJsonAsync("/transfers", new { source = Alice, target = Alice, amount = 9000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("errors").GetArrayLength());
    }
}
