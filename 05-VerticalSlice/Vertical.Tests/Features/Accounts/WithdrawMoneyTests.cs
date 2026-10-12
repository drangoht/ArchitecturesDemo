using System.Net;
using System.Net.Http.Json;
using Vertical.Api.Data;
using Vertical.Api.Features.Accounts;

namespace Vertical.Tests.Features.Accounts;

public class WithdrawMoneyTests
{
    [Fact]
    public async Task A_valid_withdrawal_debits_the_account()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient()
            .PostAsJsonAsync($"/accounts/{BankDbContext.Alice}/withdrawal", new WithdrawMoney.Request(100m));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WithdrawMoney.Response>();
        Assert.Equal(900m, result!.NewBalance);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]       // validation of the slice
    [InlineData(99_999, HttpStatusCode.BadRequest)]  // domain rule (insufficient balance)
    public async Task An_invalid_withdrawal_is_refused(int amount, HttpStatusCode expected)
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient()
            .PostAsJsonAsync($"/accounts/{BankDbContext.Alice}/withdrawal", new WithdrawMoney.Request(amount));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_account_returns_404()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient()
            .PostAsJsonAsync($"/accounts/{Guid.NewGuid()}/withdrawal", new WithdrawMoney.Request(10m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
