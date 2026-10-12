using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cell.Tests;

/// <summary>The data plane: each customer goes to ITS cell, and a failure only affects one cell.</summary>
public class RoutingTests
{
    [Fact]
    public async Task The_demo_customers_are_spread_across_the_cells()
    {
        await using var system = new CellSystem();

        var placement = await system.Placement();

        Assert.Equal("c1", placement.GetProperty("alice").GetString());
        Assert.Equal("c2", placement.GetProperty("bob").GetString());
        Assert.Equal("c3", placement.GetProperty("carla").GetString());
        Assert.Equal("c1", placement.GetProperty("david").GetString());
        Assert.Equal("c2", placement.GetProperty("emma").GetString());
    }

    [Theory]
    [InlineData("alice", "c1")]
    [InlineData("bob", "c2")]
    [InlineData("carla", "c3")]
    public async Task The_router_sends_each_customer_to_its_cell(string customer, string cell)
    {
        await using var system = new CellSystem();

        var response = await system.Client().GetAsync($"/customers/{customer}/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(cell, response.Headers.GetValues("X-Cell").Single());
    }

    [Fact]
    public async Task A_cell_only_contains_its_customers()
    {
        await using var system = new CellSystem();

        Assert.Equal(HttpStatusCode.OK, (await system.Direct("c1").GetAsync("/customers/alice/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await system.Direct("c1").GetAsync("/customers/bob/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await system.Client().GetAsync("/customers/unknown/accounts")).StatusCode);
    }

    [Fact]
    public async Task A_cell_failure_only_affects_its_customers()
    {
        await using var system = new CellSystem();
        await system.SetDown("c2");

        var statuses = new Dictionary<string, HttpStatusCode>();
        foreach (var customer in new[] { "alice", "bob", "carla", "david", "emma" })
            statuses[customer] = (await system.Client().GetAsync($"/customers/{customer}/accounts")).StatusCode;

        // Blast radius: 2 customers out of 5 (those of c2), and not all of them.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, statuses["bob"]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, statuses["emma"]);
        Assert.All(new[] { "alice", "carla", "david" }, c => Assert.Equal(HttpStatusCode.OK, statuses[c]));
    }

    [Fact]
    public async Task A_transfer_within_the_same_cell_stays_local()
    {
        await using var system = new CellSystem();

        var response = await system.Client().PostAsJsonAsync("/customers/alice/transfers",
            new { source = CellSystem.Alice, targetCustomer = "david", target = CellSystem.David, amount = 100 });

        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("crossCell").GetBoolean());
        Assert.Equal(900m, await system.Balance("alice"));
        Assert.Equal(400m, await system.Balance("david"));
    }

    [Fact]
    public async Task A_transfer_between_cells_goes_through_the_router_and_credits_only_once()
    {
        await using var system = new CellSystem();

        var response = await system.Client().PostAsJsonAsync("/customers/alice/transfers",
            new { source = CellSystem.Alice, targetCustomer = "bob", target = CellSystem.Bob, amount = 100 });
        var transfer = await response.Content.ReadFromJsonAsync<JsonElement>();
        // The same credit replayed (retry after a lost response): no effect.
        await system.Client().PostAsJsonAsync("/internal/customers/bob/credits",
            new { operationId = transfer.GetProperty("operationId").GetGuid(), account = CellSystem.Bob, amount = 100, label = "Transfer from alice" });

        Assert.True(transfer.GetProperty("crossCell").GetBoolean());
        Assert.Equal(900m, await system.Balance("alice"));
        Assert.Equal(600m, await system.Balance("bob"));
    }

    [Fact]
    public async Task If_the_beneficiary_s_cell_is_down_the_transfer_is_compensated()
    {
        await using var system = new CellSystem();
        await system.SetDown("c2");

        var response = await system.Client().PostAsJsonAsync("/customers/alice/transfers",
            new { source = CellSystem.Alice, targetCustomer = "bob", target = CellSystem.Bob, amount = 100 });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1_000m, await system.Balance("alice"));
        var movements = await system.Client().GetFromJsonAsync<JsonElement>($"/customers/alice/accounts/{CellSystem.Alice}/movements");
        Assert.Contains(movements.EnumerateArray(), m => m.GetProperty("label").GetString()!.StartsWith("Cancellation"));
    }
}
