using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sls.Functions;

namespace Sls.Tests;

public class PlatformUnderTest : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Platform:ColdStartMs", "30");
        builder.UseSetting("Platform:DelayBetweenAttemptsMs", "30");
        builder.UseSetting("Platform:MsBeforeRecycling", "60000");
    }
}

/// <summary>The complete system: functions deployed on the platform, chained through HTTP, queues and storage.</summary>
public class SystemTests
{
    private static async Task<JsonElement> WaitFor(HttpClient client, string url, Func<JsonElement, bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(15);
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
    public async Task The_first_call_is_cold_and_the_second_warm()
    {
        await using var platform = new PlatformUnderTest();
        var client = platform.CreateClient();

        var first = await client.GetAsync($"/accounts/{DemoData.Alice}");
        var second = await client.GetAsync($"/accounts/{DemoData.Alice}");

        Assert.Equal("yes", first.Headers.GetValues("X-Cold-Start").Single());
        Assert.Equal("no", second.Headers.GetValues("X-Cold-Start").Single());
    }

    [Fact]
    public async Task A_transfer_goes_through_HTTP_then_two_queues()
    {
        await using var platform = new PlatformUnderTest();
        var client = platform.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers", new { source = DemoData.Alice, target = DemoData.Bob, amount = 250m });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await WaitFor(client, response.Headers.Location!.ToString(), v => v.GetProperty("status").GetString() == "Done");
        var notifications = await WaitFor(client, $"/accounts/{DemoData.Bob}/notifications", n => n.GetArrayLength() == 1);
        Assert.Contains("received from Alice", notifications[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_import_file_is_processed_by_several_instances_in_parallel()
    {
        await using var platform = new PlatformUnderTest();
        var client = platform.CreateClient();
        var csv = new StringBuilder("source;target;amount\n");
        for (var i = 1; i <= 8; i++)
            csv.AppendLine($"{DemoData.Carla};{(i % 2 == 0 ? DemoData.Alice : DemoData.Bob)};{i * 10}");

        await client.PostAsync("/imports", new StringContent(csv.ToString(), Encoding.UTF8, "text/plain"));

        await WaitFor(client, $"/accounts/{DemoData.Carla}", c => c.GetProperty("balance").GetDecimal() == 5_000m - 360m);
        var execute = (await client.GetFromJsonAsync<JsonElement>("/platform/functions")).EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == "ExecuteTransfer");
        Assert.True(execute.GetProperty("peakInstances").GetInt32() > 1);
    }

    [Fact]
    public async Task Failed_messages_go_to_the_poison_queue_and_can_be_replayed()
    {
        await using var platform = new PlatformUnderTest();
        var client = platform.CreateClient();
        await client.PutAsJsonAsync("/platform/settings/Sms:Down", new { value = "true" });

        await client.PostAsJsonAsync("/transfers", new { source = DemoData.Carla, target = DemoData.Alice, amount = 5m });
        await WaitFor(client, "/platform/queues", f => f.TryGetProperty("notifications-poison", out var p) && p.GetProperty("messages").GetInt32() == 2);

        await client.PutAsJsonAsync("/platform/settings/Sms:Down", new { value = "false" });
        await client.PostAsync("/platform/queues/notifications-poison/replay", null);

        await WaitFor(client, $"/accounts/{DemoData.Alice}/notifications", n => n.GetArrayLength() == 1);
    }

    [Fact]
    public async Task The_bill_counts_each_invocation()
    {
        await using var platform = new PlatformUnderTest();
        var client = platform.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.GetAsync("/visits");

        var bill = await client.GetFromJsonAsync<JsonElement>("/platform/bill");

        Assert.Equal(5, bill.GetProperty("invocations").GetInt64());
        Assert.True(bill.GetProperty("totalUsd").GetDecimal() > 0);
    }
}
