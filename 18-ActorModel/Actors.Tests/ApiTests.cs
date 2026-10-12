using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Actors.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Actors.Tests;

/// <summary>The complete application: routes → messages → actors, and what the runtime reports → HTTP codes.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;

    private static readonly string Alice = DemoData.Alice.ToString(), Bob = DemoData.Bob.ToString();

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private async Task<JsonElement> Get(string url) => await _client.GetFromJsonAsync<JsonElement>(url);

    private async Task<decimal> Balance(string account) => (await Get($"/accounts/{account}")).GetProperty("balance").GetDecimal();

    private Task<HttpResponseMessage> Withdrawal(string account, decimal amount) =>
        _client.PostAsJsonAsync($"/accounts/{account}/withdrawal", new { amount });

    private Task Trap(string actor, string step, int times = 1) =>
        _client.PostAsJsonAsync("/system/failures", new { actor, step, times });

    [Fact]
    public async Task The_actors_responses_become_HTTP_codes()
    {
        Assert.Equal(HttpStatusCode.OK, (await Withdrawal(Alice, 100)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Withdrawal(Alice, 5_000)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/accounts/{Guid.NewGuid()}")).StatusCode);

        var opening = await _client.PostAsJsonAsync("/accounts", new { owner = "Zoe", initialDeposit = 50 });
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        Assert.Equal(50m, (await Get(opening.Headers.Location!.ToString())).GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task A_failure_answers_500_and_the_next_account_answers_normally()
    {
        await Trap($"account/{Alice}", "BeforeSave");

        var response = await Withdrawal(Alice, 100);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal($"account/{Alice}", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actor").GetString());
        Assert.Equal(1_000m, await Balance(Alice));
        Assert.Equal(1, (await Get("/system")).GetProperty("restarts").GetInt32());
        Assert.Contains("restarted", (await Get("/system/log"))[0].GetString());
    }

    [Fact]
    public async Task Repeated_failures_put_the_actor_in_quarantine()
    {
        await Trap($"account/{Bob}", "Reception", times: 4);  // the API's strategy: 3 restarts per minute

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.InternalServerError, (await _client.GetAsync($"/accounts/{Bob}")).StatusCode);
        for (var i = 0; i < 50 && (await Get("/system")).GetProperty("stops").GetInt32() == 0; i++)
            await Task.Delay(10);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync($"/accounts/{Bob}")).StatusCode);
        Assert.Equal(1_000m, await Balance(Alice));  // the other actors are not affected
    }

    [Fact]
    public async Task A_slowness_answers_504_without_blocking_the_other_accounts()
    {
        var client = _app.WithWebHostBuilder(b => b.UseSetting("Actors:AskTimeoutMs", "300")).CreateClient();
        await client.PostAsJsonAsync("/system/failures", new { actor = $"account/{Bob}", step = "Reception", kind = "Slowness", latencyMs = 1_000 });

        var slow = client.GetAsync($"/accounts/{Bob}");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/accounts/{Alice}")).StatusCode);

        Assert.Equal(HttpStatusCode.GatewayTimeout, (await slow).StatusCode);
        await Task.Delay(1_000);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/accounts/{Bob}")).StatusCode);  // once the slowness is over, Bob answers
    }

    [Fact]
    public async Task The_simultaneous_withdrawals_demo_never_leaves_the_balance_negative()
    {
        var report = await (await _client.PostAsJsonAsync("/demo/simultaneous-withdrawals", new { account = Alice, count = 1_000, amount = 3 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(333, report.GetProperty("accepted").GetInt32());
        Assert.Equal(1m, report.GetProperty("finalBalance").GetDecimal());
        Assert.True(report.GetProperty("minimumObservedBalance").GetDecimal() >= 0);
    }

    [Fact]
    public async Task The_crossed_transfers_demo_preserves_the_money()
    {
        var report = await (await _client.PostAsJsonAsync("/demo/crossed-transfers", new { count = 100, amount = 10 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(200, report.GetProperty("transfers").GetInt32());
        Assert.Equal(1_500m, report.GetProperty("totalBefore").GetDecimal());
        Assert.Equal(1_500m, report.GetProperty("totalAfter").GetDecimal());
    }

    [Fact]
    public async Task An_identified_transfer_can_be_replayed_without_risk()
    {
        var transfer = new { id = "invoice-42", source = Alice, target = Bob, amount = 100 };

        var first = await (await _client.PostAsJsonAsync("/transfers", transfer)).Content.ReadFromJsonAsync<JsonElement>();
        await _client.PostAsJsonAsync("/transfers", transfer);

        Assert.Equal("Completed", first.GetProperty("status").GetString());
        Assert.Equal(900m, await Balance(Alice));
        Assert.Equal("Completed", (await Get("/transfers/invoice-42")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/transfers", transfer with { id = "not valid !" })).StatusCode);
    }

    [Fact]
    public async Task Passivation_empties_the_memory_but_not_the_storage()
    {
        await Withdrawal(Alice, 100);

        var passivated = (await (await _client.PostAsync("/system/passivation", null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("passivated").GetInt32();

        Assert.Equal(2, passivated);
        Assert.Equal(0, (await Get("/system")).GetProperty("active").GetInt32());
        Assert.Contains($"account/{Alice}", (await Get("/system/storage?type=account")).EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(900m, await Balance(Alice));
        Assert.Equal(1, (await Get("/system")).GetProperty("active").GetInt32());
    }
}
