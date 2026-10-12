using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bff.Tests;

/// <summary>
/// The three processes of the demo, in memory: the services, and the two BFFs plugged into them
/// (their HTTP client goes through the services' test server instead of the network).
/// </summary>
public sealed class BffSystem : IAsyncDisposable
{
    public WebApplicationFactory<Bff.Services.Marker> Services { get; }
    public WebApplicationFactory<Bff.Web.Marker> Web { get; }
    public WebApplicationFactory<Bff.Mobile.Marker> Mobile { get; }

    public BffSystem(int latencyMs = 0, int offersBudgetMs = 2_000)
    {
        Services = new WebApplicationFactory<Bff.Services.Marker>()
            .WithWebHostBuilder(b => b.UseSetting("Services:LatencyMs", latencyMs.ToString()));
        Web = new WebApplicationFactory<Bff.Web.Marker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Bff:OffersBudgetMs", offersBudgetMs.ToString());
            b.ConfigureTestServices(s => s.AddHttpClient<Bff.Web.ServicesClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Services.Server.CreateHandler()));
        });
        Mobile = new WebApplicationFactory<Bff.Mobile.Marker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Bff:OfferBudgetMs", offersBudgetMs.ToString());
            b.ConfigureTestServices(s => s.AddHttpClient<Bff.Mobile.ServicesClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Services.Server.CreateHandler()));
        });
    }

    /// <summary>A browser: it keeps the cookies and adds the anti-CSRF header, as wwwroot/index.html does.</summary>
    public HttpClient Browser(bool antiCsrf = true)
    {
        var client = Web.CreateClient();
        if (antiCsrf)
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }

    public async Task<HttpClient> LoggedInBrowser(string user = "alice")
    {
        var browser = Browser();
        var response = await browser.PostAsJsonAsync("/bff/login", new { user, password = "demo" });
        response.EnsureSuccessStatusCode();
        return browser;
    }

    /// <summary>The mobile application: it gets its token and sends it itself with each request.</summary>
    public async Task<HttpClient> LoggedInPhone(string user = "alice")
    {
        var phone = Mobile.CreateClient();
        var token = await (await phone.PostAsJsonAsync("/mobile/v1/login", new { user, password = "demo" }))
            .Content.ReadFromJsonAsync<Bff.Mobile.DownstreamToken>();
        phone.DefaultRequestHeaders.Authorization = new("Bearer", token!.Token);
        return phone;
    }

    public async Task<string> ServicesToken(string user)
    {
        var response = await Services.CreateClient().PostAsJsonAsync("/identity/token", new { user, password = "demo" });
        return (await response.Content.ReadFromJsonAsync<Bff.Web.DownstreamToken>())!.Token;
    }

    public static int Header(HttpResponseMessage response, string name) => int.Parse(response.Headers.GetValues(name).Single());

    public async ValueTask DisposeAsync()
    {
        await Web.DisposeAsync();
        await Mobile.DisposeAsync();
        await Services.DisposeAsync();
    }
}
