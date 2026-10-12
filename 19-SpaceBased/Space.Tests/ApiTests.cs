using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Space.Bank;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Space.Tests;

/// <summary>The complete application, without automatic elasticity or latencies (the tests drive it).</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("Elasticity:EvaluationPeriodMs", "0")
        .UseSetting("Platform:ReplicationLatencyMs", "0")
        .UseSetting("Platform:DatabaseLatencyMs", "0")
        .UseSetting("Platform:ProcessingTimeMs", "0"));

    private HttpClient _client = null!;
    private static readonly string Alice = DemoData.Alice.ToString();

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private async Task<JsonElement> Get(string url) => await _client.GetFromJsonAsync<JsonElement>(url);

    [Fact]
    public async Task Each_response_tells_which_unit_handled_it()
    {
        var response = await _client.GetAsync($"/accounts/{Alice}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("^u[12]$", response.Headers.GetValues("X-Unit").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/accounts/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 5_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/accounts", new { owner = "Zoe", initialDeposit = 10 })).StatusCode);
    }

    [Fact]
    public async Task Database_down_requests_go_through_but_a_cold_start_is_impossible()
    {
        await _client.PostAsync("/grid/database/failure", null);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 100 })).StatusCode);
        Assert.Equal(1, (await Get("/grid")).GetProperty("pumpPending").GetInt32());

        await _client.PostAsync("/grid/total-failure", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync($"/accounts/{Alice}")).StatusCode);

        await _client.PostAsync("/grid/database/restore", null);
        Assert.Equal(900m, (await Get($"/accounts/{Alice}")).GetProperty("balance").GetDecimal());
        Assert.Equal(2, (await Get("/grid")).GetProperty("coldStarts").GetInt32());
    }

    [Fact]
    public async Task The_concurrent_withdrawals_demo_with_affinity_loses_nothing()
    {
        await _client.PostAsync("/grid/units", null);

        var report = await (await _client.PostAsJsonAsync("/demo/concurrent-withdrawals", new { account = Alice, count = 300, amount = 3 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(333 - 33, report.GetProperty("accepted").GetInt32());
        Assert.Equal(100m, report.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal([100m], report.GetProperty("balanceInGrid").EnumerateArray().Select(s => s.GetDecimal()));
        Assert.Equal(100m, report.GetProperty("balanceInDatabase").GetDecimal());
        Assert.Equal(0, report.GetProperty("lostUpdates").GetInt32());
        Assert.Single(report.GetProperty("unitsUsed").EnumerateArray());
    }

    [Fact]
    public async Task Units_can_be_added_stopped_and_brought_down()
    {
        Assert.Equal("u3", (await (await _client.PostAsync("/grid/units", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unit").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/grid/units/u1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/grid/units/u2/failure", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/grid/units/u9")).StatusCode);

        var units = (await Get("/grid")).GetProperty("units").EnumerateArray().Select(u => u.GetProperty("id").GetString());
        Assert.Equal(["u3"], units);
        Assert.Equal(1_000m, (await Get($"/accounts/{Alice}")).GetProperty("balance").GetDecimal());
        Assert.Contains((await Get("/grid/log")).EnumerateArray(), e => e.GetString()!.Contains("u2 FAILED"));
    }

    [Fact]
    public async Task The_comparison_shows_the_same_version_everywhere_at_rest()
    {
        await _client.PostAsJsonAsync($"/accounts/{Alice}/withdrawal", new { amount = 100 });
        for (var i = 0; i < 200 && await Get("/grid") is var g
             && g.GetProperty("replicationPending").GetInt32() + g.GetProperty("pumpPending").GetInt32() > 0; i++)
            await Task.Delay(5);  // wait for rest: replication delivered, pump drained

        var comparison = await Get($"/grid/comparison/{Alice}");

        var versions = comparison.GetProperty("replicas").EnumerateArray().Select(r => r.GetProperty("version").GetString())
            .Append(comparison.GetProperty("database").GetProperty("version").GetString());
        Assert.Single(versions.Distinct());
        Assert.StartsWith("v2@u", versions.First());
    }
}
