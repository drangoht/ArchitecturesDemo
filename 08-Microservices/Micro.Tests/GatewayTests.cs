using System.Net;
using System.Net.Http.Json;
using Micro.Gateway;

namespace Micro.Tests;

public class GatewayTests
{
    [Fact]
    public async Task The_gateway_routes_each_prefix_to_its_service()
    {
        var downstream = FakeHttp.AlwaysOk();
        await using var service = new ServiceUnderTest<Marker>(new() { ["downstream"] = downstream });
        var client = service.CreateClient();

        await client.GetAsync("/accounts/123");
        await client.PostAsJsonAsync("/transfers", new { amount = 10 });

        Assert.Equal(
            [("localhost:5201", "/accounts/123"), ("localhost:5202", "/transfers")],
            downstream.Calls.Select(a => (a.Host, a.Path)));
        Assert.Equal("{\"amount\":10}", downstream.Calls.Last().Body);
    }

    [Fact]
    public async Task The_internal_API_of_the_services_is_not_exposed()
    {
        var downstream = FakeHttp.AlwaysOk();
        await using var service = new ServiceUnderTest<Marker>(new() { ["downstream"] = downstream });

        var response = await service.CreateClient().PostAsJsonAsync("/internal/accounts/123/debits", new { amount = 1000 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(downstream.Calls);
    }

    [Fact]
    public async Task An_unreachable_service_gives_502()
    {
        var unreachable = new FakeHttp((_, _) => throw new HttpRequestException("Connection refused"));
        await using var service = new ServiceUnderTest<Marker>(new() { ["downstream"] = unreachable });

        var response = await service.CreateClient().GetAsync("/notifications/123");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
