using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Choreo.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Choreo.Tests;

/// <summary>The complete application. Automatic expiry disabled: the tests do not depend on a timer.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app =
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Bank:ExpiryPeriodMs", "0"));
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private Task<HttpResponseMessage> Transfer(Guid source, Guid target, decimal amount, bool wait = true) =>
        _client.PostAsJsonAsync($"/transfers{(wait ? "?wait=true" : "")}", new { source, target, amount });

    private async Task<JsonElement> Get(string url) => await _client.GetFromJsonAsync<JsonElement>(url);

    /// <summary>The handling is asynchronous: poll until the condition is true (5 s at most).</summary>
    private async Task<JsonElement> WaitFor(string url, Func<JsonElement, bool> condition)
    {
        for (var i = 0; ; i++)
        {
            var response = await _client.GetAsync(url);
            if (response.IsSuccessStatusCode && await response.Content.ReadFromJsonAsync<JsonElement>() is var json && condition(json))
                return json;
            Assert.True(i < 500, $"{url}: condition never met");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Requesting_a_transfer_answers_202_and_the_rest_happens_without_the_caller()
    {
        var response = await Transfer(DemoData.Alice, DemoData.Carla, 100m, wait: false);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var transfer = await WaitFor(response.Headers.Location!.ToString(), v => v.GetProperty("history").GetArrayLength() == 6);

        Assert.Equal("Settled", transfer.GetProperty("state").GetProperty("status").GetString());
        Assert.Equal(10, (await Get($"/loyalty/{DemoData.Alice}")).GetProperty("points").GetInt32());
        Assert.Single((await Get("/notifications")).EnumerateArray());
    }

    [Fact]
    public async Task The_analyst_decides_through_the_API()
    {
        var id = (await (await Transfer(DemoData.Alice, DemoData.Bob, 15_000m)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        Assert.Equal([id], (await Get("/compliance/pending")).EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/compliance/{id}/decision", new { approved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/compliance/{id}/decision", new { approved = true })).StatusCode);
    }

    [Fact]
    public async Task A_failed_service_shows_in_the_dead_letters_and_the_stuck_transfers()
    {
        await _client.PostAsJsonAsync("/chaos", new { exchangeDown = true });

        var id = (await (await Transfer(DemoData.Alice, DemoData.Bob, 200m, wait: false)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var deadLetters = await WaitFor("/bus/dead-letters", l => l.GetArrayLength() > 0);
        Assert.Equal(("Exchange", id), (deadLetters[0].GetProperty("subscriber").GetString(), deadLetters[0].GetProperty("transfer").GetString()));
        Assert.Equal("InProgress", (await Get($"/transfers/{id}")).GetProperty("state").GetProperty("status").GetString());
        Assert.Contains((await Get("/transfers/stuck?delaySeconds=0")).EnumerateArray(), v => v.GetProperty("id").GetString() == id);
        Assert.Equal(200m, (await Get($"/accounts/{DemoData.Alice}")).GetProperty("reserved").GetDecimal());
    }

    [Fact]
    public async Task The_bus_shows_who_listens_to_what()
    {
        var subscribers = (await Get("/bus")).EnumerateArray().ToDictionary(a => a.GetProperty("subscriber").GetString()!,
            a => a.GetProperty("listens").EnumerateArray().Select(e => e.GetString()).ToList());

        Assert.Equal(["ComplianceRefused", "CurrenciesConverted", "ExchangeFailed", "TransferRequested"], subscribers["Accounts"]);
        Assert.Equal(["TransferSettled"], subscribers["Loyalty"]);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/transfers/unknown")).StatusCode);
    }
}
