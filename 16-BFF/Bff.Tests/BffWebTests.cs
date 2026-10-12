using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bff.Services;

namespace Bff.Tests;

public class BffWebTests
{
    [Fact]
    public async Task Login_sets_an_HttpOnly_cookie_and_returns_no_token()
    {
        await using var system = new BffSystem();

        var response = await system.Browser().PostAsJsonAsync("/bff/login", new { user = "alice", password = "demo" });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bff-session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("""{"user":"alice"}""", body);
    }

    [Fact]
    public async Task Without_a_session_the_API_answers_401_and_not_a_redirect()
    {
        await using var system = new BffSystem();

        var response = await system.Browser().GetAsync("/api/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Without_the_anti_CSRF_header_a_request_is_refused_even_with_a_session()
    {
        await using var system = new BffSystem();
        var browser = await system.LoggedInBrowser();
        browser.DefaultRequestHeaders.Remove("X-CSRF");  // what a booby-trapped form on another site would do

        var response = await browser.PostAsJsonAsync("/api/transfers", new { source = Bank.AliceChecking, target = Bank.BobChecking, amount = 100 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-CSRF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_dashboard_aggregates_seven_calls_made_in_parallel()
    {
        const int latency = 150;
        await using var system = new BffSystem(latencyMs: latency);
        var browser = await system.LoggedInBrowser();

        var response = await browser.GetAsync("/api/dashboard");
        var dashboard = await response.Content.ReadFromJsonAsync<JsonElement>();

        // profile, then in parallel: advisor, offers, 2 accounts, 2 lists of operations
        Assert.Equal(7, BffSystem.Header(response, "X-Downstream-Calls"));
        // In parallel: 2 waves (the profile, then all the rest), so about 2 latencies; chained, it would take 7.
        var duration = BffSystem.Header(response, "X-Total-Duration-Ms");
        Assert.True(duration < 4 * latency, $"{duration} ms: the calls should have overlapped.");
        Assert.True(BffSystem.Header(response, "X-Downstream-Cumulative-Ms") >= 7 * latency);
        Assert.Equal(2, dashboard.GetProperty("accounts").GetArrayLength());
        Assert.Equal(10, dashboard.GetProperty("accounts")[0].GetProperty("operations").GetArrayLength());
        Assert.Equal("Claire Dubois", dashboard.GetProperty("customer").GetProperty("advisor").GetProperty("name").GetString());
        Assert.NotEqual(0, dashboard.GetProperty("spendingByCategory").GetArrayLength());
        Assert.Equal(2, dashboard.GetProperty("offers").GetArrayLength());
        Assert.Equal(0, dashboard.GetProperty("degraded").GetArrayLength());
    }

    [Fact]
    public async Task An_offers_failure_degrades_the_dashboard_without_making_it_fail()
    {
        await using var system = new BffSystem();
        await system.Services.CreateClient().PutAsJsonAsync("/chaos", new { offersDown = true });
        var browser = await system.LoggedInBrowser();

        var dashboard = await browser.GetFromJsonAsync<JsonElement>("/api/dashboard");

        Assert.Equal(JsonValueKind.Null, dashboard.GetProperty("offers").ValueKind);
        Assert.Equal("offers", dashboard.GetProperty("degraded")[0].GetString());
        Assert.Equal(2, dashboard.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task A_service_that_is_too_slow_is_given_up_at_the_end_of_its_budget()
    {
        await using var system = new BffSystem(latencyMs: 300, offersBudgetMs: 100);
        var browser = await system.LoggedInBrowser();

        var dashboard = await browser.GetFromJsonAsync<JsonElement>("/api/dashboard");

        Assert.Equal("offers", dashboard.GetProperty("degraded")[0].GetString());
        Assert.Equal(2, dashboard.GetProperty("accounts").GetArrayLength());
    }

    [Fact]
    public async Task A_transfer_goes_through_the_session_and_the_user_s_rights()
    {
        await using var system = new BffSystem();
        var browser = await system.LoggedInBrowser();

        var transfer = await browser.PostAsJsonAsync("/api/transfers", new { source = Bank.AliceChecking, target = Bank.BobChecking, amount = 25 });
        // Bob is logged in elsewhere; Alice tries to debit Bob's account: the services refuse, the BFF has no special privilege.
        var debitFromBob = await browser.PostAsJsonAsync("/api/transfers", new { source = Bank.BobChecking, target = Bank.AliceChecking, amount = 25 });

        Assert.Equal(975m, (await transfer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, debitFromBob.StatusCode);
    }

    [Fact]
    public async Task After_logout_a_stolen_cookie_is_worthless()
    {
        await using var system = new BffSystem();
        var login = await system.Browser().PostAsJsonAsync("/bff/login", new { user = "alice", password = "demo" });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bff-session=")).Split(';')[0];
        var thief = system.Web.CreateClient(new() { HandleCookies = false });
        thief.DefaultRequestHeaders.Add("X-CSRF", "1");
        thief.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await thief.GetAsync("/bff/user")).StatusCode);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logout.Headers.Add("Cookie", cookie);
        await thief.SendAsync(logout);

        // The session is deleted on the server side: the same cookie, replayed, leads nowhere any more.
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/bff/user")).StatusCode);
    }

    [Fact]
    public async Task The_site_is_served_by_its_BFF_on_the_same_origin()
    {
        await using var system = new BffSystem();

        var page = await system.Browser(antiCsrf: false).GetStringAsync("/");

        Assert.Contains("X-CSRF", page);
        Assert.DoesNotContain("localStorage.setItem", page);
    }
}
