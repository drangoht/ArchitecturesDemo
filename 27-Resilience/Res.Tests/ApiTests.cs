using System.Net;
using System.Net.Http.Json;
using Res.Bank;
using Res.FraudSimulator;

namespace Res.Tests;

/// <summary>The bank and its strategies, through HTTP, in front of the simulated partner in each of its moods.</summary>
public class ApiTests
{
    private static Task<HttpResponseMessage> Transfer(BankApplication app, HttpClient client, decimal amount) =>
        app.Clock.Drive(client.PostAsJsonAsync("/transfers", new { source = Ledger.Alice, target = Ledger.Bob, amount }));

    private static Task<ResilienceJson?> State(HttpClient client) => client.GetFromJsonAsync<ResilienceJson>("/resilience");

    [Fact]
    public async Task With_a_healthy_partner_the_transfer_is_checked_with_one_call()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await Transfer(app, client, 250m);

        Assert.Equal(new OutcomeJson("accepted", 750m, 750m), await response.Content.ReadFromJsonAsync<OutcomeJson>());
        Assert.Equal(1, app.Partner.CallsReceived);
    }

    [Fact]
    public async Task A_flaky_partner_is_absorbed_by_the_retries()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        app.Partner.Set(PartnerMode.Flaky, failures: 2);

        var response = await Transfer(app, client, 250m);

        Assert.Equal("accepted", (await response.Content.ReadFromJsonAsync<OutcomeJson>())!.Status);
        Assert.Equal(3, app.Partner.CallsReceived);
        Assert.Equal(2, (await State(client))!.Events.Count(e => e.StartsWith("retry")));
    }

    [Fact]
    public async Task A_slow_partner_times_out_and_a_small_transfer_goes_through_unverified()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        app.Partner.Set(PartnerMode.Slow);

        var response = await Transfer(app, client, 200m);

        Assert.Equal(new OutcomeJson("accepted-unverified", 800m, 700m), await response.Content.ReadFromJsonAsync<OutcomeJson>());
        Assert.Equal(4, (await State(client))!.Events.Count(e => e.StartsWith("timeout")));
        var alice = await client.GetFromJsonAsync<AccountJson>($"/accounts/{Ledger.Alice}");
        Assert.Equal("Transfer to Bob (unverified): -200", alice!.Operations.Single());
    }

    [Fact]
    public async Task With_the_partner_down_a_large_transfer_is_refused_with_503_and_Retry_After()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        app.Partner.Set(PartnerMode.Down);

        var response = await Transfer(app, client, 600m);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(1000m, (await client.GetFromJsonAsync<AccountJson>($"/accounts/{Ledger.Alice}"))!.Balance);
    }

    [Fact]
    public async Task Once_open_the_circuit_spares_the_partner_then_a_trial_closes_it_when_the_partner_is_back()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        app.Partner.Set(PartnerMode.Down);

        for (var i = 0; i < 5; i++)
            await Transfer(app, client, 10m);   // each one falls back: accepted, unverified

        var state = await State(client);
        Assert.Equal(("Open", 5), (state!.Circuit, state.PartnerCallsReceived));   // 4 attempts, then 1, then none

        app.Partner.Set(PartnerMode.Healthy);
        app.Clock.Advance(TimeSpan.FromSeconds(30));
        var response = await Transfer(app, client, 10m);

        Assert.Equal("accepted", (await response.Content.ReadFromJsonAsync<OutcomeJson>())!.Status);
        Assert.Equal(("Closed", 6), ((await State(client))!.Circuit, app.Partner.CallsReceived));
    }

    [Fact]
    public async Task A_refusal_by_the_partner_is_a_business_answer_and_is_not_retried()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await Transfer(app, client, 800m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Refused by the fraud check.", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, app.Partner.CallsReceived);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(5_001, HttpStatusCode.BadRequest)]
    [InlineData(1_001, HttpStatusCode.BadRequest)]   // insufficient balance
    public async Task An_invalid_transfer_never_reaches_the_partner(int amount, HttpStatusCode expected)
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await Transfer(app, client, amount);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, app.Partner.CallsReceived);
    }

    [Fact]
    public async Task The_transfer_limit_is_checked_before_the_balance()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await Transfer(app, client, 5_001m);

        Assert.Contains("A transfer cannot exceed 5000 €.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_account_is_a_404_without_calling_the_partner()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await app.Clock.Drive(client.PostAsJsonAsync("/transfers",
            new { source = Ledger.Alice, target = Guid.NewGuid(), amount = 10 }));

        Assert.Equal((HttpStatusCode.NotFound, 0), (response.StatusCode, app.Partner.CallsReceived));
    }

    [Fact]
    public async Task When_the_bulkhead_is_full_the_next_transfer_falls_back_at_once()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();
        app.Partner.Set(PartnerMode.Slow);   // without moving the clock, the calls stay in progress

        var waiting = Enumerable.Range(0, 4)
            .Select(_ => client.PostAsJsonAsync("/transfers", new { source = Ledger.Alice, target = Ledger.Bob, amount = 10 }))
            .ToList();
        await WaitUntil(async () => await State(client) is { InProgress: 2, Queued: 2 });
        var fifth = await client.PostAsJsonAsync("/transfers", new { source = Ledger.Alice, target = Ledger.Bob, amount = 10 })
            .WaitAsync(TimeSpan.FromSeconds(5));   // refused at once, without moving the clock

        Assert.Equal("accepted-unverified", (await fifth.Content.ReadFromJsonAsync<OutcomeJson>())!.Status);
        Assert.Contains((await State(client))!.Events, e => e.StartsWith("bulkhead"));
        await app.Clock.Drive(Task.WhenAll(waiting));
        Assert.Equal(2, app.Partner.MaxInProgress);
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > giveUp)
                throw new TimeoutException("The condition never became true.");
            await Task.Delay(5);
        }
    }
}
