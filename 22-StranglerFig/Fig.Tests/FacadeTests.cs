using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fig.Facade;
using Fig.Modern;
using static Fig.Tests.FigSystem;

namespace Fig.Tests;

/// <summary>The facade: who answers what, the live switch both ways, the shadow mode, the removal of the legacy system.</summary>
public class FacadeTests
{
    private static string ServedBy(HttpResponseMessage response) => response.Headers.GetValues("X-Served-By").Single();

    [Fact]
    public async Task A_route_nobody_took_over_goes_to_the_legacy_system()
    {
        await using var s = new FigSystem();

        var response = await s.Client.GetAsync("/branches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("legacy", ServedBy(response));
        Assert.Equal(1, (await s.Json(s.Admin, "/facade/progress"))["undeclaredCalls"]!.GetValue<long>());
    }

    [Fact]
    public async Task The_internal_routes_are_not_exposed_to_the_clients()
    {
        await using var s = new FigSystem();

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync("/internal/log")).StatusCode);
        var fee = await s.Client.PostAsJsonAsync("/internal/fees", new Dictionary<string, object>
        {
            ["ACCNUM"] = Alice, ["AMTCTS"] = 100_000, ["OPELBL"] = "HACKING"
        });

        Assert.Equal(HttpStatusCode.NotFound, fee.StatusCode);
        Assert.Equal(1_000m, await s.Balance(s.LegacyDirect, Alice));
    }

    [Fact]
    public async Task Switching_a_route_changes_who_answers_without_restarting_or_changing_anything_for_the_client()
    {
        await using var s = new FigSystem();
        await s.Synchronize();

        await s.Switch("lookup", Mode.Legacy);
        var byLegacy = await s.Client.GetAsync($"/accounts/{Alice}");
        await s.Switch("lookup", Mode.Modern);
        var byModern = await s.Client.GetAsync($"/accounts/{Alice}");

        Assert.Equal("legacy", ServedBy(byLegacy));
        Assert.Equal("modern", ServedBy(byModern));
        Assert.Equal(await byLegacy.Content.ReadAsStringAsync(), await byModern.Content.ReadAsStringAsync());
        var state = await s.Admin.GetFromJsonAsync<List<RouteState>>("/facade/routes", Options);
        Assert.Equal((1, 1), state!.Where(r => r.Name == "lookup").Select(r => (r.LegacyCalls, r.ModernCalls)).Single());
    }

    [Fact]
    public async Task In_shadow_mode_the_client_receives_the_legacy_response_and_both_are_compared()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        await s.Switch("operations", Mode.Shadow);

        var response = await s.Client.GetAsync($"/accounts/{Alice}/operations");

        Assert.Equal("legacy", ServedBy(response));
        var report = await s.WaitForComparisons(b => b["compared"]!.GetValue<long>() == 1);
        Assert.Equal(1, report["identical"]!.GetValue<long>());
        Assert.Equal(0, report["divergences"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_divergence_is_reported_without_the_client_seeing_it()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        await s.Switch("operations", Mode.Shadow);
        // A gap in the modern service: an operation it alone knows (a takeover bug, for example).
        s.ModernStore.ApplyFromLegacy([new OperationRecorded(Alice,
            new Operation("X-1", s.Clock.GetUtcNow(), "GHOST", -1m, Origin.Legacy))], s.ModernStore.LegacyPosition);

        var response = await s.Client.GetStringAsync($"/accounts/{Alice}/operations");

        Assert.Equal(await s.LegacyDirect.GetStringAsync($"/accounts/{Alice}/operations"), response);   // the client sees nothing
        var report = await s.WaitForComparisons(b => b["divergences"]!.GetValue<long>() == 1);
        var divergence = report["lastDivergences"]![0]!;
        Assert.Equal("operations", divergence["route"]!.GetValue<string>());
        Assert.Contains("GHOST", divergence["modernBody"]!.GetValue<string>());
    }

    [Fact]
    public async Task In_shadow_mode_a_slow_or_failed_modern_service_does_not_bother_the_client()
    {
        await using var s = new FigSystem(shadowBudgetMs: 2_000);
        await s.Synchronize();
        await s.Switch("operations", Mode.Shadow);
        await s.Client.GetAsync($"/accounts/{Alice}/operations");   // first call: startups, compilations
        await s.WaitForComparisons(b => b["compared"]!.GetValue<long>() == 1);

        s.FacadeToModern.Latency = TimeSpan.FromSeconds(5);      // beyond the shadow budget (2 s)
        var stopwatch = Stopwatch.StartNew();
        var slow = await s.Client.GetAsync($"/accounts/{Alice}/operations");
        stopwatch.Stop();
        s.FacadeToModern.Latency = TimeSpan.Zero;
        s.FacadeToModern.Cut = true;
        var down = await s.Client.GetAsync($"/accounts/{Alice}/operations");

        Assert.Equal(HttpStatusCode.OK, slow.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1.5), $"The client waited {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        var report = await s.WaitForComparisons(b => b["abandoned"]!.GetValue<long>() == 2);
        Assert.Equal(0, report["divergences"]!.GetValue<long>());
    }

    [Fact]
    public async Task Shadow_mode_is_refused_for_a_write()
    {
        await using var s = new FigSystem();

        var response = await s.Admin.PutAsJsonAsync("/facade/routes/transfers", new { mode = "Shadow" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("twice", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Steering_the_migration_requires_the_admin_key()
    {
        await using var s = new FigSystem();

        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/facade/routes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.PutAsJsonAsync("/facade/routes/transfers", new { mode = "Modern" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.PutAsJsonAsync("/facade/routes/unknown", new { mode = "Modern" })).StatusCode);
    }

    [Fact]
    public async Task Rolling_back_a_route_loses_no_transfer()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        await s.Switch("transfers", Mode.Modern);
        var byModern = await s.Client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 100 });
        Assert.Equal("modern", ServedBy(byModern));
        await s.Synchronize();

        // A problem in production: the route is given back to the legacy system. It must know the transfer made by the modern one.
        await s.Switch("transfers", Mode.Legacy);
        var refused = await s.Client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 950 });

        Assert.Equal("legacy", ServedBy(refused));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);   // 1,000 - 100 = 900: the 950 transfer is impossible
        Assert.Contains("INSUFFICIENT BALANCE", await refused.Content.ReadAsStringAsync());
        Assert.Equal(900m, await s.Balance(s.LegacyDirect, Alice));
    }

    [Fact]
    public async Task Writing_on_one_side_and_reading_on_the_other_shows_a_late_balance_until_the_synchronization()
    {
        // LIMIT of the demo, shown rather than hidden: two routes touching the same data, served by two
        // different systems. Hence the advice to switch TOGETHER the reads and the writes of the same data.
        await using var s = new FigSystem();
        await s.Synchronize();
        await s.Switch("lookup", Mode.Modern);
        await s.Switch("transfers", Mode.Legacy);

        var transfer = await s.Client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 100 });
        var before = await s.Balance(s.Client, Alice);
        await s.Synchronize();
        var after = await s.Balance(s.Client, Alice);

        Assert.Equal(900m, JsonNode.Parse(await transfer.Content.ReadAsStringAsync())!["sourceBalance"]!.GetValue<decimal>());
        Assert.Equal(1_000m, before);   // the modern one does not know yet
        Assert.Equal(900m, after);
    }

    [Fact]
    public async Task The_legacy_system_is_only_removable_when_nothing_calls_it_any_more()
    {
        await using var s = new FigSystem();
        await s.Client.GetAsync($"/accounts/{Alice}/statement");   // the statement is still served by the legacy system
        await s.Switch("lookup", Mode.Modern);
        Assert.False((await s.Json(s.Admin, "/facade/progress"))["legacyRemovable"]!.GetValue<bool>());

        await s.SwitchAll(Mode.Modern);
        var allMigrated = await s.Json(s.Admin, "/facade/progress");
        await s.Client.GetAsync("/branches");   // a function nobody listed
        var afterBranches = await s.Json(s.Admin, "/facade/progress");

        Assert.Equal(100, allMigrated["percentage"]!.GetValue<int>());
        Assert.True(allMigrated["legacyRemovable"]!.GetValue<bool>());
        Assert.False(afterBranches["legacyRemovable"]!.GetValue<bool>());
        Assert.Equal(1, afterBranches["legacyCallsSinceLastSwitch"]!.GetValue<long>());
    }

    [Fact]
    public async Task Once_everything_is_migrated_the_legacy_system_can_be_switched_off()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        await s.SwitchAll(Mode.Modern);
        s.FacadeToLegacy.Cut = true;     // the legacy system is switched off
        s.ModernToLegacy.Cut = true;

        var transfer = await s.Client.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 100 });
        var account = await s.Client.GetAsync($"/accounts/{Alice}");
        var operations = await s.Client.GetAsync($"/accounts/{Alice}/operations");
        var statement = await s.Client.GetAsync($"/accounts/{Alice}/statement");
        var branches = await s.Client.GetAsync("/branches");

        foreach (var response in new[] { transfer, account, operations, statement })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("modern", ServedBy(response));
        }
        Assert.Equal(900m, JsonNode.Parse(await account.Content.ReadAsStringAsync())!["balance"]!.GetValue<decimal>());
        Assert.Equal(HttpStatusCode.BadGateway, branches.StatusCode);   // the forgotten function: it had to be migrated too
    }

    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
