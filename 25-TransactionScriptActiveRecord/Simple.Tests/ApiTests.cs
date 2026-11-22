using System.Net;
using System.Net.Http.Json;
using Simple.Data;

namespace Simple.Tests;

// The same routes under /scripts and /records: same answers, same database.
public class ApiTests
{
    [Theory]
    [InlineData("scripts")]
    [InlineData("records")]
    public async Task A_withdrawal_returns_the_new_balance(string style)
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient()
            .PostAsJsonAsync($"/{style}/accounts/{Database.Alice}/withdrawal", new { amount = 100 });

        response.EnsureSuccessStatusCode();
        Assert.Equal(new WithdrawalJson(Database.Alice, 900m), await response.Content.ReadFromJsonAsync<WithdrawalJson>());
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("records")]
    public async Task A_transfer_returns_and_saves_both_balances(string style)
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client
            .PostAsJsonAsync($"/{style}/transfers", new { source = Database.Alice, target = Database.Bob, amount = 250 });

        response.EnsureSuccessStatusCode();
        Assert.Equal(new TransferJson(750m, 750m), await response.Content.ReadFromJsonAsync<TransferJson>());
        var alice = await client.GetFromJsonAsync<AccountJson>($"/{style}/accounts/{Database.Alice}");
        var bob = await client.GetFromJsonAsync<AccountJson>($"/{style}/accounts/{Database.Bob}");
        Assert.Equal((750m, 750m), (alice!.Balance, bob!.Balance));
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("records")]
    public async Task Opening_an_account_returns_201_and_its_address(string style)
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync($"/{style}/accounts", new { owner = "Chloe", initialDeposit = 50 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await client.GetFromJsonAsync<AccountJson>(response.Headers.Location);
        Assert.Equal(new OperationJson("Initial deposit", 50m), account!.LatestOperations.Single());
    }

    [Theory]
    [InlineData("scripts", "records")]
    [InlineData("records", "scripts")]
    public async Task What_one_style_writes_the_other_reads(string writer, string reader)
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        await client.PostAsJsonAsync($"/{writer}/accounts/{Database.Alice}/withdrawal", new { amount = 100 });
        var alice = await client.GetFromJsonAsync<AccountJson>($"/{reader}/accounts/{Database.Alice}");

        Assert.Equal(900m, alice!.Balance);
        Assert.Equal(new OperationJson("Withdrawal", -100m), alice.LatestOperations.Single());
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("records")]
    public async Task A_broken_rule_returns_400_with_the_message(string style)
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient()
            .PostAsJsonAsync($"/{style}/accounts/{Database.Bob}/withdrawal", new { amount = 99_999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new ErrorJson("Insufficient balance."), await response.Content.ReadFromJsonAsync<ErrorJson>());
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("records")]
    public async Task A_transfer_to_an_unknown_account_returns_404_and_debits_nothing(string style)
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync($"/{style}/transfers",
            new { source = Database.Alice, target = Guid.Empty, amount = 50 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var alice = await client.GetFromJsonAsync<AccountJson>($"/{style}/accounts/{Database.Alice}");
        Assert.Equal(1000m, alice!.Balance);
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("records")]
    public async Task An_unknown_account_returns_404(string style)
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().GetAsync($"/{style}/accounts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
