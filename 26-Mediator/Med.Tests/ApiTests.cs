using System.Net;
using System.Net.Http.Json;
using Med.Bank.Data;

namespace Med.Tests;

public class ApiTests
{
    [Fact]
    public async Task A_withdrawal_returns_the_new_balance()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().PostAsJsonAsync($"/accounts/{BankDb.Alice}/withdrawal", new { amount = 100 });

        response.EnsureSuccessStatusCode();
        Assert.Contains("\"newBalance\":900", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_validation_error_becomes_a_400_with_the_fields_in_error()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().PostAsJsonAsync("/accounts", new { owner = "", initialDeposit = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"Owner\"", body);
        Assert.Contains("\"InitialDeposit\"", body);
    }

    [Fact]
    public async Task A_broken_business_rule_becomes_a_400_with_its_message()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().PostAsJsonAsync($"/accounts/{BankDb.Bob}/withdrawal", new { amount = 99_999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Insufficient balance.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unknown_account_becomes_a_404()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().GetAsync($"/accounts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Opening_an_account_returns_201_its_address_and_the_account()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/accounts", new { owner = "Chloe", initialDeposit = 50 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AccountJson>();
        var read = await client.GetFromJsonAsync<AccountJson>(response.Headers.Location);
        Assert.Equal((created!.Id, 50m), (read!.Id, read.Balance));
    }

    [Fact]
    public async Task A_transfer_reaches_Bob_with_his_notification_and_the_journal_shows_the_requests()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers", new { source = BankDb.Alice, target = BankDb.Bob, amount = 1000 });

        response.EnsureSuccessStatusCode();
        var bob = await client.GetFromJsonAsync<AccountJson>($"/accounts/{BankDb.Bob}");
        Assert.Equal((1500m, "You received 1000 € from Alice."), (bob!.Balance, bob.Notifications.Single()));
        Assert.Single((await client.GetFromJsonAsync<List<string>>("/compliance/alerts"))!);
        var journal = await client.GetStringAsync("/journal");
        Assert.Contains("\"request\":\"Transfer\",\"outcome\":\"ok\"", journal);
        Assert.Contains("\"request\":\"GetAccount\",\"outcome\":\"ok\"", journal);
    }
}
