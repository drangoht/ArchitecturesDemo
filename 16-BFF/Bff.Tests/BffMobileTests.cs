using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bff.Mobile;
using Bff.Services;

namespace Bff.Tests;

public class BffMobileTests
{
    [Fact]
    public async Task Without_a_token_the_application_gets_401()
    {
        await using var system = new BffSystem();

        var response = await system.Mobile.CreateClient().GetAsync("/mobile/v1/home");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_mobile_home_fits_in_one_request_and_a_compact_response()
    {
        await using var system = new BffSystem();
        var phone = await system.LoggedInPhone();
        var browser = await system.LoggedInBrowser();

        var response = await phone.GetAsync("/mobile/v1/home");
        var json = await response.Content.ReadAsStringAsync();
        var home = JsonDocument.Parse(json).RootElement;
        var web = await (await browser.GetAsync("/api/dashboard")).Content.ReadAsStringAsync();

        Assert.Equal(5, BffSystem.Header(response, "X-Downstream-Calls"));   // profile, 2 accounts, 2 × 3 operations
        Assert.Equal(9_000m, home.GetProperty("totalBalance").GetDecimal());
        Assert.Equal(3, home.GetProperty("latestOperations").GetArrayLength());
        Assert.DoesNotContain("iban", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CARD ", json);
        Assert.True(json.Length * 5 < web.Length, $"Mobile: {json.Length} bytes, web: {web.Length} bytes.");
    }

    [Fact]
    public async Task The_v2_adds_an_offer_banner_without_changing_the_v1()
    {
        await using var system = new BffSystem();
        var phone = await system.LoggedInPhone();

        var v1 = await phone.GetFromJsonAsync<JsonElement>("/mobile/v1/home");
        var v2 = await phone.GetFromJsonAsync<JsonElement>("/mobile/v2/home");

        Assert.False(v1.TryGetProperty("offer", out _));
        Assert.Equal("Fixed-rate mortgage", v2.GetProperty("offer").GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_cursor_goes_through_all_the_operations_without_duplicates()
    {
        await using var system = new BffSystem();
        var phone = await system.LoggedInPhone();
        var read = new List<JsonElement>();
        var pages = 0;
        string? cursor = null;

        do
        {
            var url = $"/mobile/v1/accounts/{Bank.AliceChecking}/operations" + (cursor is null ? "" : $"?cursor={Uri.EscapeDataString(cursor)}");
            var page = await phone.GetFromJsonAsync<JsonElement>(url);
            read.AddRange(page.GetProperty("operations").EnumerateArray());
            cursor = page.TryGetProperty("next", out var next) ? next.GetString() : null;
            pages++;
        } while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(25, read.Count);
        Assert.Equal(25, read.Select(o => o.GetRawText()).Distinct().Count());
    }

    [Theory]
    [InlineData("CARD CARREFOUR 0910 PARIS 15", "Carrefour")]
    [InlineData("CARD AMAZON EU SARL 0910", "Amazon EU")]
    [InlineData("DIRECT DEBIT EDF", "EDF")]
    [InlineData("DIRECT DEBIT FREE MOBILE", "Free Mobile")]
    [InlineData("ATM WITHDRAWAL 0810 LYON", "Cash withdrawal")]
    [InlineData("SEPA TRANSFER SALARY ACME SAS", "Salary")]
    [InlineData("UNKNOWN LABEL", "UNKNOWN LABEL")]
    public void The_raw_bank_label_is_shortened_for_the_phone(string raw, string expected) =>
        Assert.Equal(expected, Screens.ShortLabel(raw));
}
