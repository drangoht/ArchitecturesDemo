using System.Net;
using System.Net.Http.Json;
using Vertical.Api.Features.Accounts;

namespace Vertical.Tests.Features.Accounts;

// The tests follow the same organization as the code: one test file per slice.
public class OpenAccountTests
{
    [Fact]
    public async Task Opening_an_account_returns_201_and_the_account_can_be_read()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/accounts", new OpenAccount.Request("Chloe", 50m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<OpenAccount.Response>();
        var lookup = await client.GetFromJsonAsync<GetAccount.Response>($"/accounts/{account!.Id}");
        Assert.Equal(50m, lookup!.Balance);
        Assert.Equal("Initial deposit", lookup.LatestOperations.Single().Label);
    }

    [Fact]
    public async Task An_invalid_request_returns_the_validation_errors()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().PostAsJsonAsync("/accounts", new OpenAccount.Request("", 1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Owner", body);
        Assert.Contains("InitialDeposit", body);
    }
}
