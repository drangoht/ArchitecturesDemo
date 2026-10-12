using System.Net;
using System.Net.Http.Json;
using Vertical.Api.Data;
using Vertical.Api.Features.Accounts;
using Vertical.Api.Features.Transfers;

namespace Vertical.Tests.Features.Transfers;

public class MakeTransferTests
{
    [Fact]
    public async Task A_transfer_moves_the_money_and_records_the_operations()
    {
        await using var app = new BankApplication();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/transfers",
            new MakeTransfer.Request(BankDbContext.Alice, BankDbContext.Bob, 250m));

        var result = await response.Content.ReadFromJsonAsync<MakeTransfer.Response>();
        Assert.Equal(new MakeTransfer.Response(750m, 750m), result);

        var bob = await client.GetFromJsonAsync<GetAccount.Response>($"/accounts/{BankDbContext.Bob}");
        Assert.Equal("Transfer from Alice", bob!.LatestOperations.Single().Label);
    }

    [Fact]
    public async Task A_transfer_to_the_same_account_is_refused_by_validation()
    {
        await using var app = new BankApplication();

        var response = await app.CreateClient().PostAsJsonAsync("/transfers",
            new MakeTransfer.Request(BankDbContext.Alice, BankDbContext.Alice, 10m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
