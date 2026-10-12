using System.Net;
using System.Net.Http.Json;
using Bff.Services;

namespace Bff.Tests;

public class ServicesAndArchitectureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Bearer any.old.thing")]
    [InlineData("Bearer eyJTdWIiOiJhbGljZSJ9.c2lnbmF0dXJl")]  // "alice" payload with a fake signature
    public async Task Without_a_valid_token_the_services_answer_401(string? authorization)
    {
        await using var system = new BffSystem();
        var client = system.Services.CreateClient();
        if (authorization is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/profiles/me")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_gives_no_token()
    {
        await using var system = new BffSystem();

        var response = await system.Services.CreateClient().PostAsJsonAsync("/identity/token", new { user = "alice", password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_only_gives_access_to_the_accounts_of_its_owner()
    {
        await using var system = new BffSystem();
        var client = system.Services.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await system.ServicesToken("bob"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/accounts/{Bank.BobChecking}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/accounts/{Bank.AliceChecking}")).StatusCode);
    }

    [Fact]
    public void Each_BFF_only_knows_the_services_and_the_other_BFF_through_HTTP()
    {
        // One BFF per application, each to its own team: no code reference between them, nor to the services.
        string[] forbidden = ["Bff.Services", "Bff.Web", "Bff.Mobile"];
        foreach (var bff in new[] { typeof(Bff.Web.Marker).Assembly, typeof(Bff.Mobile.Marker).Assembly })
        {
            var references = bff.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.Empty(references.Intersect(forbidden));
        }
    }
}
