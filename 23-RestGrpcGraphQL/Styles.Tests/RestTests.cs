using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Styles.Tests;

public class RestTests : IAsyncLifetime
{
    private readonly TestSystem _system = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _system.DisposeAsync();

    [Fact]
    public async Task Each_resource_has_its_address_and_gives_those_of_the_linked_resources()
    {
        var customer = (await _system.Http.GetFromJsonAsync<JsonObject>("/customers/alice"))!;
        var accounts = (await _system.Http.GetFromJsonAsync<JsonArray>(customer["_links"]!["accounts"]!.GetValue<string>()))!;
        var operations = (await _system.Http.GetFromJsonAsync<JsonArray>(accounts[0]!["_links"]!["operations"]!.GetValue<string>() + "?limit=2"))!;

        Assert.Equal(["10001", "10002", "10003"], accounts.Select(c => c!["number"]!.GetValue<string>()));
        Assert.Equal(["Bookshop", "Electricity"], operations.Select(o => o!["label"]!.GetValue<string>()));
    }

    [Fact]
    public async Task The_representation_of_an_account_is_complete_and_the_same_for_all_clients()
    {
        var account = (await _system.Http.GetFromJsonAsync<JsonObject>("/accounts/10001"))!;

        // The home screen only needs the label and the balance: it receives all the rest too (over-fetching).
        Assert.Equal(["number", "label", "type", "iban", "balance", "currency", "openingDate", "overdraftLimit", "owner", "_links"],
            account.Select(p => p.Key));
    }

    [Fact]
    public async Task A_transfer_creates_a_201_resource_whose_address_is_in_Location()
    {
        var response = await _system.Http.PostAsJsonAsync("/transfers", new { source = "10001", target = "20001", amount = 40m, label = "Tickets" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/transfers/V001", response.Headers.Location!.ToString());
        var readBack = (await _system.Http.GetFromJsonAsync<JsonObject>(response.Headers.Location))!;
        Assert.Equal((40m, 1586.05m), (readBack["amount"]!.GetValue<decimal>(), readBack["sourceBalance"]!.GetValue<decimal>()));
    }

    [Theory]
    [InlineData("10001", "99999", 10, HttpStatusCode.NotFound, "UNKNOWN_ACCOUNT")]
    [InlineData("10001", "20001", -5, HttpStatusCode.BadRequest, "INVALID_AMOUNT")]
    [InlineData("10001", "10001", 10, HttpStatusCode.BadRequest, "SAME_ACCOUNT")]
    [InlineData("10002", "10001", 9000, HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_BALANCE")]
    public async Task Errors_are_HTTP_statuses_with_a_problem_json_body(string source, string target, decimal amount,
        HttpStatusCode status, string code)
    {
        var response = await _system.Http.PostAsJsonAsync("/transfers", new { source, target, amount });

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(((int)status, code), (problem["status"]!.GetValue<int>(), problem["code"]!.GetValue<string>()));
    }

    [Fact]
    public async Task The_ETag_avoids_transferring_again_an_account_that_has_not_changed()
    {
        var first = await _system.Http.GetAsync("/accounts/10001");
        var etag = first.Headers.ETag!.Tag;

        var unchanged = await Read(etag);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());

        await _system.Rest.Transfer("10001", "20001", 1m, "Coffee");

        var modified = await Read(etag);
        Assert.Equal(HttpStatusCode.OK, modified.StatusCode);
        Assert.NotEqual(etag, modified.Headers.ETag!.Tag);

        async Task<HttpResponseMessage> Read(string knownEtag)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/accounts/10001");
            request.Headers.IfNoneMatch.ParseAdd(knownEtag);
            return await _system.Http.SendAsync(request);
        }
    }
}
