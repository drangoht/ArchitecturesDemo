using System.Net;
using System.Net.Http.Json;
using static Fig.Tests.FigSystem;

namespace Fig.Tests;

/// <summary>As long as both systems live, they must say the same thing: both ways.</summary>
public class SynchronizationTests
{
    [Fact]
    public async Task The_first_cycle_reloads_the_whole_legacy_system()
    {
        await using var s = new FigSystem();
        Assert.Equal(HttpStatusCode.NotFound, (await s.ModernDirect.GetAsync($"/accounts/{Alice}")).StatusCode);

        var report = await s.Synchronize();

        Assert.Equal(4, report.LinesRead);   // 2 account openings, 2 initial deposits
        foreach (var number in new[] { Alice, Bob })
            Assert.Equal(await s.LegacyDirect.GetStringAsync($"/accounts/{number}"), await s.ModernDirect.GetStringAsync($"/accounts/{number}"));
    }

    [Fact]
    public async Task What_an_internal_batch_of_the_legacy_system_writes_reaches_the_modern_one()
    {
        await using var s = new FigSystem();
        await s.Synchronize();

        await s.ChargeFee(Alice, 250, "Account maintenance fee");
        Assert.Equal(1_000m, await s.Balance(s.ModernDirect, Alice));   // not yet
        var report = await s.Synchronize();

        Assert.Equal(1, report.FactsApplied);
        Assert.Equal(997.5m, await s.Balance(s.ModernDirect, Alice));
        Assert.Equal(await s.LegacyDirect.GetStringAsync($"/accounts/{Alice}/operations"),
            await s.ModernDirect.GetStringAsync($"/accounts/{Alice}/operations"));
    }

    [Fact]
    public async Task A_transfer_made_in_the_modern_service_is_forwarded_to_the_legacy_system()
    {
        await using var s = new FigSystem();
        await s.Synchronize();

        (await s.ModernDirect.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 50 })).EnsureSuccessStatusCode();
        Assert.Equal(1_000m, await s.Balance(s.LegacyDirect, Alice));   // not yet
        var report = await s.Synchronize();

        Assert.Equal(2, report.Forwarded);
        Assert.Equal(950m, await s.Balance(s.LegacyDirect, Alice));
        Assert.Equal(550m, await s.Balance(s.LegacyDirect, Bob));
        Assert.Equal(await s.ModernDirect.GetStringAsync($"/accounts/{Alice}/statement"), await s.LegacyDirect.GetStringAsync($"/accounts/{Alice}/statement"));
    }

    [Fact]
    public async Task The_echo_of_a_modern_transfer_does_not_come_back_twice()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        (await s.ModernDirect.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 50 })).EnsureSuccessStatusCode();

        await s.Synchronize();   // forwards the transfer to the legacy system, which writes it in its log
        var echo = await s.Synchronize();   // reads these two rows again: they are our own operations
        await s.Synchronize();

        Assert.Equal(2, echo.LinesRead);
        Assert.Equal(0, echo.FactsApplied);
        Assert.Equal(950m, await s.Balance(s.ModernDirect, Alice));
        Assert.Equal(2, (await s.Json(s.ModernDirect, $"/accounts/{Alice}/operations")).AsArray().Count);
        Assert.Equal(await s.LegacyDirect.GetStringAsync($"/accounts/{Alice}/operations"),
            await s.ModernDirect.GetStringAsync($"/accounts/{Alice}/operations"));
    }

    [Fact]
    public async Task If_the_legacy_system_is_unreachable_the_operations_wait_for_the_next_cycle()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        (await s.ModernDirect.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 50 })).EnsureSuccessStatusCode();

        s.ModernToLegacy.WritesCut = true;   // the log can be read, the import fails
        await Assert.ThrowsAsync<HttpRequestException>(s.Synchronize);
        Assert.Equal(2, s.ModernStore.ToForward().Count);
        Assert.Equal(1_000m, await s.Balance(s.LegacyDirect, Alice));

        s.ModernToLegacy.WritesCut = false;
        await s.Synchronize();
        Assert.Empty(s.ModernStore.ToForward());
        Assert.Equal(950m, await s.Balance(s.LegacyDirect, Alice));
    }

    [Fact]
    public async Task A_batch_sent_again_after_a_lost_response_is_imported_only_once()
    {
        await using var s = new FigSystem();
        await s.Synchronize();
        (await s.ModernDirect.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 50 })).EnsureSuccessStatusCode();

        s.ModernToLegacy.WriteResponsesLost = true;   // the legacy system imports, but the modern one does not know it
        await Assert.ThrowsAsync<HttpRequestException>(s.Synchronize);
        Assert.Equal(950m, await s.Balance(s.LegacyDirect, Alice));
        s.ModernToLegacy.WriteResponsesLost = false;
        await s.Synchronize();                          // the same batch leaves again

        Assert.Equal(950m, await s.Balance(s.LegacyDirect, Alice));
        Assert.Equal(550m, await s.Balance(s.LegacyDirect, Bob));
    }
}
