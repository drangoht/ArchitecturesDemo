using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Saga.Api;

namespace Saga.Tests;

public class OrchestratorUnderTest : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Saga:PauseBetweenStepsMs", "0");
        builder.UseSetting("Saga:InitialDelayMs", "20");
    }
}

/// <summary>The real orchestrator, with its background loop.</summary>
public class ApiTests
{
    private static async Task<JsonElement> WaitForStatus(HttpClient client, string url, params string[] statuses)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        JsonElement saga;
        do
        {
            saga = await client.GetFromJsonAsync<JsonElement>(url);
            if (statuses.Contains(saga.GetProperty("status").GetString()))
                return saga;
            await Task.Delay(30);
        } while (DateTime.UtcNow < limit);
        Assert.Fail($"Expected status {string.Join("/", statuses)}, got: {saga}");
        return saga;
    }

    [Fact]
    public async Task A_saga_started_over_HTTP_goes_all_the_way()
    {
        await using var app = new OrchestratorUnderTest();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers", new { source = DemoData.Alice, target = DemoData.Carla, amount = 100m });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var saga = await WaitForStatus(client, response.Headers.Location!.ToString(), "Completed");
        Assert.All(saga.GetProperty("steps").EnumerateArray(), e => Assert.Equal("done", e.GetProperty("state").GetString()));
    }

    [Fact]
    public async Task The_analyst_decision_arrives_over_HTTP()
    {
        await using var app = new OrchestratorUnderTest();
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/transfers", new { source = DemoData.Alice, target = DemoData.Bob, amount = 12_000m });
        var url = response.Headers.Location!.ToString();
        await WaitForStatus(client, url, "Waiting");

        var decision = await client.PostAsJsonAsync($"{url}/compliance", new { approved = false, comment = "origin of funds not justified" });

        Assert.Equal(HttpStatusCode.NoContent, decision.StatusCode);
        var saga = await WaitForStatus(client, url, "Cancelled");
        Assert.Contains("origin of funds", saga.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_orchestrator_stopped_then_restarted_resumes_the_saga()
    {
        await using var app = new OrchestratorUnderTest();
        var client = app.CreateClient();
        await client.PostAsync("/admin/orchestrator/stop", null);

        var response = await client.PostAsJsonAsync("/transfers", new { source = DemoData.Alice, target = DemoData.Bob, amount = 50m });
        var url = response.Headers.Location!.ToString();
        await Task.Delay(300);
        Assert.Equal("Running", (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("status").GetString());

        await client.PostAsync("/admin/orchestrator/restart", null);

        await WaitForStatus(client, url, "Completed");
    }
}
