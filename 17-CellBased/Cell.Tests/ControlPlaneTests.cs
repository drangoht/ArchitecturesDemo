using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cell.Tests;

/// <summary>The control plane: placement, adding cells, migration, deployment in waves.</summary>
public class ControlPlaneTests
{
    [Fact]
    public async Task A_new_customer_goes_to_the_least_loaded_cell()
    {
        await using var system = new CellSystem();

        var response = await system.Client().PostAsJsonAsync("/customers", new { id = "farid", name = "Farid", initialDeposit = 50 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("c3", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cell").GetString());
        Assert.Equal(50m, await system.Balance("farid"));
    }

    [Fact]
    public async Task When_the_cells_are_full_one_is_added_instead_of_enlarging_them()
    {
        await using var system = new CellSystem();
        foreach (var id in new[] { "g1", "g2", "g3", "g4" })   // 5 + 4 = 9 = 3 cells × 3 places
            (await system.Client().PostAsJsonAsync("/customers", new { id, name = id })).EnsureSuccessStatusCode();

        var refusal = await system.Client().PostAsJsonAsync("/customers", new { id = "g5", name = "g5" });
        var addition = await system.Client().PostAsJsonAsync("/platform/cells", new { id = "c4", url = "http://localhost:5234", capacity = 3 });
        var accepted = await system.Client().PostAsJsonAsync("/customers", new { id = "g5", name = "g5" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refusal.StatusCode);
        Assert.Contains("full", await refusal.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, addition.StatusCode);
        Assert.Equal("c4", (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cell").GetString());
    }

    [Fact]
    public async Task A_closed_cell_no_longer_receives_new_customers()
    {
        await using var system = new CellSystem();
        await system.Client().PostAsync("/platform/cells/c3/close", null);

        var response = await system.Client().PostAsJsonAsync("/customers", new { id = "farid", name = "Farid" });

        Assert.NotEqual("c3", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cell").GetString());
        Assert.Equal(HttpStatusCode.OK, (await system.Client().GetAsync("/customers/carla/accounts")).StatusCode);  // its customers are still served
    }

    [Fact]
    public async Task Migrating_a_customer_moves_its_data_and_its_routing()
    {
        await using var system = new CellSystem();
        await system.Client().PostAsJsonAsync($"/customers/bob/accounts/{CellSystem.Bob}/withdrawal", new { amount = 50 });

        var report = await (await system.Client().PostAsJsonAsync("/platform/customers/bob/migrate", new { to = "c3" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var after = await system.Client().GetAsync("/customers/bob/accounts");

        Assert.Equal("c2", report.GetProperty("from").GetString());
        Assert.Equal("c3", after.Headers.GetValues("X-Cell").Single());
        Assert.Equal(450m, await system.Balance("bob"));
        Assert.Equal(1, (await system.Client().GetFromJsonAsync<JsonElement>($"/customers/bob/accounts/{CellSystem.Bob}/movements")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await system.Direct("c2").GetAsync("/customers/bob/accounts")).StatusCode);
    }

    [Fact]
    public async Task The_already_processed_operations_follow_the_customer_into_its_new_cell()
    {
        await using var system = new CellSystem();
        var transfer = await (await system.Client().PostAsJsonAsync("/customers/alice/transfers",
            new { source = CellSystem.Alice, targetCustomer = "bob", target = CellSystem.Bob, amount = 100 })).Content.ReadFromJsonAsync<JsonElement>();
        await system.Client().PostAsJsonAsync("/platform/customers/bob/migrate", new { to = "c3" });

        await system.Client().PostAsJsonAsync("/internal/customers/bob/credits",
            new { operationId = transfer.GetProperty("operationId").GetGuid(), account = CellSystem.Bob, amount = 100, label = "replay" });

        Assert.Equal(600m, await system.Balance("bob"));
    }

    [Fact]
    public async Task The_health_probe_detects_the_bug_of_version_1_1()
    {
        await using var system = new CellSystem();
        await system.Direct("c1").PutAsJsonAsync("/admin/version", new { version = "1.1" });

        var health = await system.Direct("c1").GetFromJsonAsync<JsonElement>("/health");
        await system.Client().PostAsJsonAsync($"/customers/alice/accounts/{CellSystem.Alice}/withdrawal", new { amount = 10 });

        Assert.False(health.GetProperty("healthy").GetBoolean());
        Assert.Equal(980m, await system.Balance("alice"));   // the bug: 10 € withdrawn, 20 € debited
        Assert.Equal(500m, await system.Balance("bob"));     // c2 did not receive this version
    }

    [Fact]
    public async Task A_faulty_deployment_stops_at_the_canary_cell()
    {
        await using var system = new CellSystem();

        var report = await (await system.Client().PostAsJsonAsync("/platform/deployments", new { version = "1.1" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Cancelled", report.GetProperty("status").GetString());
        Assert.Equal(1, report.GetProperty("waves").GetArrayLength());   // the 2nd wave never started
        Assert.Contains("rollback", report.GetProperty("waves")[0].GetProperty("result").GetString());
        foreach (var cell in new[] { "c1", "c2", "c3" })
            Assert.Equal("1.0", await system.Version(cell));
        // Only the canary cell ran the faulty version; its customers are the only ones who could have suffered from it.
        Assert.Equal(["1.0", "1.1", "1.0"], await system.History("c1"));
        Assert.Equal(["1.0"], await system.History("c2"));
        Assert.Equal(["1.0"], await system.History("c3"));
    }

    [Fact]
    public async Task A_healthy_deployment_goes_wave_by_wave()
    {
        await using var system = new CellSystem();

        var report = await (await system.Client().PostAsJsonAsync("/platform/deployments", new { version = "1.2" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Completed", report.GetProperty("status").GetString());
        Assert.Equal(["c1"], report.GetProperty("waves")[0].GetProperty("cells").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(["c2", "c3"], report.GetProperty("waves")[1].GetProperty("cells").EnumerateArray().Select(c => c.GetString()));
        foreach (var cell in new[] { "c1", "c2", "c3" })
            Assert.Equal("1.2", await system.Version(cell));
    }

    [Fact]
    public async Task A_sick_cell_stops_the_deployment_at_its_wave()
    {
        await using var system = new CellSystem();
        await system.SetDown("c3");   // down but reachable: its probe no longer answers

        var report = await (await system.Client().PostAsJsonAsync("/platform/deployments", new { version = "1.2" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Cancelled", report.GetProperty("status").GetString());
        Assert.Equal("healthy", report.GetProperty("waves")[0].GetProperty("result").GetString());
        Assert.Equal("1.2", await system.Version("c1"));   // wave 1, healthy, is kept
        Assert.Equal("1.0", await system.Version("c2"));   // wave 2 was rolled back
    }

    [Fact]
    public void The_router_and_the_cells_share_no_code()
    {
        Assert.DoesNotContain(typeof(Cell.Router.Marker).Assembly.GetReferencedAssemblies(), a => a.Name == "Cell.App");
        Assert.DoesNotContain(typeof(Cell.App.Marker).Assembly.GetReferencedAssemblies(), a => a.Name == "Cell.Router");
    }
}
