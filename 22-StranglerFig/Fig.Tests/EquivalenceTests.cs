using System.Net.Http.Json;
using static Fig.Tests.FigSystem;

namespace Fig.Tests;

/// <summary>
/// The CONTRACT tests: for each route the modern service takes over, it must answer exactly like the legacy system,
/// to the character (status, content type, body), errors included. It is the condition for switching a
/// route without the clients noticing.
/// </summary>
public class EquivalenceTests
{
    /// <summary>A varied history, written on both sides, then synchronized.</summary>
    private static async Task<FigSystem> SystemWithHistory()
    {
        var s = new FigSystem();
        await s.Synchronize();
        s.Clock.Advance(TimeSpan.FromMinutes(5));
        await s.ChargeFee(Alice, 250, "Monthly account maintenance fees (essential plan)");   // label truncated to 30
        s.Clock.Advance(TimeSpan.FromMinutes(5));
        (await s.LegacyDirect.PostAsJsonAsync("/transfers", new { source = Alice, target = Bob, amount = 120.5m })).EnsureSuccessStatusCode();
        await s.Synchronize();
        s.Clock.Advance(TimeSpan.FromMinutes(5));
        // Written in the legacy system BEFORE the next transfer, but known to the modern one AFTER it: the arrival order is not
        // the order of the dates, and the contract sorts by date.
        await s.ChargeFee(Alice, 99, "Intervention fee");
        s.Clock.Advance(TimeSpan.FromMinutes(5));
        // "70.10": the modern service must answer 947.1 like the legacy system, and not 947.10.
        (await s.ModernDirect.PostAsJsonAsync("/transfers", new { source = Bob, target = Alice, amount = 70.10m })).EnsureSuccessStatusCode();
        // On 31 October at 11:30 pm UTC, it is already 0:30 am on 1 November in Paris (winter time): November statement.
        s.Clock.Set(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero));
        await s.ChargeFee(Bob, 1_000, "Card fee");
        await s.Synchronize();
        await s.Synchronize();
        return s;
    }

    public static TheoryData<string> Reads => new()
    {
        $"/accounts/{Alice}",
        $"/accounts/{Bob}",
        $"/accounts/{Alice}/operations",
        $"/accounts/{Bob}/operations",
        $"/accounts/{Alice}/statement?month=2026-10",
        $"/accounts/{Bob}/statement?month=2026-10",
        $"/accounts/{Bob}/statement?month=2026-11",
        $"/accounts/{Bob}/statement",
        $"/accounts/{Alice}/statement?month=2026-09",
        $"/accounts/{Alice}/statement?month=october",
        "/accounts/99999",
        "/accounts/99999/operations",
        "/accounts/99999/statement"
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task A_read_gives_the_same_response_on_both_sides(string path)
    {
        await using var s = await SystemWithHistory();

        var legacy = await s.LegacyDirect.GetAsync(path);
        var modern = await s.ModernDirect.GetAsync(path);

        Assert.Equal(legacy.StatusCode, modern.StatusCode);
        Assert.Equal(legacy.Content.Headers.ContentType, modern.Content.Headers.ContentType);
        Assert.Equal(await legacy.Content.ReadAsStringAsync(), await modern.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string, decimal> RefusedTransfers => new()
    {
        { Alice, Bob, 5_000m },     // INSUFFICIENT BALANCE
        { Alice, Bob, 0m },         // INVALID AMOUNT
        { Alice, Bob, -10m },
        { Alice, Bob, 1.005m },
        { Alice, Alice, 10m },      // IDENTICAL ACCOUNTS
        { Alice, "99999", 10m },    // UNKNOWN ACCOUNT
        { "99999", Bob, 10m }
    };

    [Theory]
    [MemberData(nameof(RefusedTransfers))]
    public async Task A_refused_transfer_is_refused_with_the_same_error_on_both_sides(string source, string target, decimal amount)
    {
        await using var s = await SystemWithHistory();
        var request = new { source, target, amount };

        var legacy = await s.LegacyDirect.PostAsJsonAsync("/transfers", request);
        var modern = await s.ModernDirect.PostAsJsonAsync("/transfers", request);

        Assert.False(legacy.IsSuccessStatusCode);
        Assert.Equal(legacy.StatusCode, modern.StatusCode);
        Assert.Equal(await legacy.Content.ReadAsStringAsync(), await modern.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_inherited_contract_keeps_the_habits_of_the_legacy_system()
    {
        await using var s = await SystemWithHistory();

        var operations = (await s.Json(s.ModernDirect, $"/accounts/{Alice}/operations")).AsArray();
        var fee = operations.Single(o => o!["label"]!.GetValue<string>().StartsWith("MONTHLY"))!;

        Assert.Equal("MONTHLY ACCOUNT MAINTENANCE FE", fee["label"]!.GetValue<string>());   // uppercase, 30 characters
        Assert.Equal("2026-10-12T10:05:00", fee["date"]!.GetValue<string>());                // Paris time, without time zone
        Assert.Equal("-2.5", fee["amount"]!.ToJsonString());                                 // and not -2.50
    }

    [Fact]
    public async Task An_accepted_transfer_gives_the_same_balance_but_not_the_same_reference()
    {
        await using var s = await SystemWithHistory();
        var request = new { source = Alice, target = Bob, amount = 100 };

        var legacy = await s.Json(s.LegacyDirect, "/accounts/" + Alice);   // same starting point on both sides
        Assert.Equal(legacy.ToJsonString(), (await s.Json(s.ModernDirect, "/accounts/" + Alice)).ToJsonString());
        var legacyResponse = await (await s.LegacyDirect.PostAsJsonAsync("/transfers", request)).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var modernResponse = await (await s.ModernDirect.PostAsJsonAsync("/transfers", request)).Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(legacyResponse!["sourceBalance"].ToString(), modernResponse!["sourceBalance"].ToString());
        // The reference is opaque to the clients: each system has its own numbering, with no possible collision.
        Assert.StartsWith("TRF-0", legacyResponse["reference"].ToString());
        Assert.StartsWith("TRF-N", modernResponse["reference"].ToString());
    }
}
