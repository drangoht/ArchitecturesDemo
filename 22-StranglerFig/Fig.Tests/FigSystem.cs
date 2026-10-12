using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fig.Facade;
using Fig.Modern;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Fig.Tests;

/// <summary>A clock moved forward by hand: both systems date their operations with it.</summary>
public sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset instant) => _now = instant;

    public void Advance(TimeSpan duration) => _now += duration;
}

/// <summary>The state of an HTTP link between two processes: cut, slow, or letting only the reads through.</summary>
public sealed class LinkState
{
    public bool Cut { get; set; }
    /// <summary>The writes fail before arriving (the legacy system goes down between reading the log and the import).</summary>
    public bool WritesCut { get; set; }
    /// <summary>The writes arrive and are processed, but the response gets lost on the way.</summary>
    public bool WriteResponsesLost { get; set; }
    public TimeSpan Latency { get; set; }
}

public sealed class Link(LinkState state, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var write = request.Method != HttpMethod.Get;
        if (state.Cut || (write && state.WritesCut))
            throw new HttpRequestException("Link cut (test)");
        if (state.Latency > TimeSpan.Zero)
            await Task.Delay(state.Latency, cancellation);
        var response = await base.SendAsync(request, cancellation);
        if (write && state.WriteResponsesLost)
        {
            response.Dispose();   // the request was processed, but the caller will never know
            throw new HttpRequestException("Response lost (test)");
        }
        return response;
    }
}

/// <summary>
/// The three processes of the demo, in memory: the legacy system, the modern service (plugged into the legacy one) and the
/// facade (plugged into both). Each link can be cut or slowed down; the synchronization is triggered by hand.
/// </summary>
public sealed class FigSystem : IAsyncDisposable
{
    public const string Alice = "10001";
    public const string Bob = "10002";

    // Monday 12 October 2026, 10 am in Paris (summer time).
    public AdjustableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));

    public LinkState ModernToLegacy { get; } = new();
    public LinkState FacadeToLegacy { get; } = new();
    public LinkState FacadeToModern { get; } = new();

    public WebApplicationFactory<Fig.Legacy.Marker> Legacy { get; }
    public WebApplicationFactory<Fig.Modern.Marker> Modern { get; }
    public WebApplicationFactory<Fig.Facade.Marker> Facade { get; }

    private HttpClient? _client, _admin, _legacyDirect, _modernDirect;

    public FigSystem(int shadowBudgetMs = 500)
    {
        Legacy = new WebApplicationFactory<Fig.Legacy.Marker>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock)));
        Modern = new WebApplicationFactory<Fig.Modern.Marker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Synchronization:IntervalMs", "0");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<TimeProvider>(Clock);
                s.AddHttpClient<LegacyClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new Link(ModernToLegacy, Legacy.Server.CreateHandler()));
            });
        });
        Facade = new WebApplicationFactory<Fig.Facade.Marker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Facade:ShadowBudgetMs", shadowBudgetMs.ToString());
            b.ConfigureTestServices(s =>
            {
                s.AddHttpClient("legacy").ConfigurePrimaryHttpMessageHandler(() => new Link(FacadeToLegacy, Legacy.Server.CreateHandler()));
                s.AddHttpClient("modern").ConfigurePrimaryHttpMessageHandler(() => new Link(FacadeToModern, Modern.Server.CreateHandler()));
            });
        });
    }

    /// <summary>A client: it only knows the facade.</summary>
    public HttpClient Client => _client ??= Facade.CreateClient();

    public HttpClient Admin => _admin ??= CreateAdmin();

    public HttpClient LegacyDirect => _legacyDirect ??= Legacy.CreateClient();

    public HttpClient ModernDirect => _modernDirect ??= Modern.CreateClient();

    public Store ModernStore => Modern.Services.GetRequiredService<Store>();

    public Task<SyncReport> Synchronize() => Modern.Services.GetRequiredService<Synchronization>().Run();

    public async Task Switch(string route, Mode mode)
    {
        var response = await Admin.PutAsJsonAsync($"/facade/routes/{route}", new { mode = mode.ToString() });
        response.EnsureSuccessStatusCode();
    }

    public async Task SwitchAll(Mode mode)
    {
        foreach (var route in new[] { "lookup", "operations", "statement", "transfers" })
            await Switch(route, mode);
    }

    /// <summary>The legacy system's night batch: it writes without going through the facade.</summary>
    public async Task ChargeFee(string number, long cents, string label)
    {
        var response = await LegacyDirect.PostAsJsonAsync("/internal/fees", new Dictionary<string, object>
        {
            ["ACCNUM"] = number, ["AMTCTS"] = cents, ["OPELBL"] = label
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonNode> Json(HttpClient client, string path) => JsonNode.Parse(await client.GetStringAsync(path))!;

    public async Task<decimal> Balance(HttpClient client, string number) =>
        (await Json(client, $"/accounts/{number}"))["balance"]!.GetValue<decimal>();

    /// <summary>The shadow mode comparisons happen after the response to the client: wait until they are done.</summary>
    public async Task<JsonNode> WaitForComparisons(Func<JsonNode, bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var report = await Json(Admin, "/facade/comparisons");
            if (condition(report))
                return report;
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException($"Expected comparisons not obtained: {report.ToJsonString()}");
            await Task.Delay(20);
        }
    }

    private HttpClient CreateAdmin()
    {
        var client = Facade.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "demo");
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Facade.DisposeAsync();
        await Modern.DisposeAsync();
        await Legacy.DisposeAsync();
    }
}
