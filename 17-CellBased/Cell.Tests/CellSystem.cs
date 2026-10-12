using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cell.Tests;

/// <summary>
/// The complete system in memory: four cells (c4 in reserve, not registered at first) and the router.
/// The router reaches each cell through its test server; the cells reach the router in the same way.
/// </summary>
public sealed class CellSystem : IAsyncDisposable
{
    public const string Alice = "11111111-1111-1111-1111-111111111111";
    public const string Bob = "22222222-2222-2222-2222-222222222222";
    public const string David = "55555555-5555-5555-5555-555555555555";

    public Dictionary<string, WebApplicationFactory<Cell.App.Marker>> Cells { get; } = [];
    public WebApplicationFactory<Cell.Router.Marker> Router { get; }

    public CellSystem()
    {
        foreach (var id in new[] { "c1", "c2", "c3", "c4" })
            Cells[id] = new WebApplicationFactory<Cell.App.Marker>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Cell:Id", id);
                b.ConfigureTestServices(s => s.AddHttpClient("router").ConfigurePrimaryHttpMessageHandler(() => Router!.Server.CreateHandler()));
            });

        Router = new WebApplicationFactory<Cell.Router.Marker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Deployment:ObservationMs", "0");
            b.ConfigureTestServices(s =>
            {
                foreach (var (id, cell) in Cells)
                    s.AddHttpClient($"cell-{id}").ConfigurePrimaryHttpMessageHandler(() => cell.Server.CreateHandler());
            });
        });

        // Start the router right away: it places the demo customers in the cells, as at the real launch.
        _ = Router.Server;
    }

    /// <summary>A customer of the bank: it only knows the router.</summary>
    public HttpClient Client() => Router.CreateClient();

    public HttpClient Direct(string cell) => Cells[cell].CreateClient();

    public async Task<decimal> Balance(string customer) =>
        (await Client().GetFromJsonAsync<JsonElement>($"/customers/{customer}/accounts"))[0].GetProperty("balance").GetDecimal();

    public async Task<JsonElement> Placement() => await Client().GetFromJsonAsync<JsonElement>("/platform/placement");

    public async Task SetDown(string cell, bool down = true) =>
        (await Direct(cell).PutAsJsonAsync("/admin/outage", new { down })).EnsureSuccessStatusCode();

    public async Task<string> Version(string cell) =>
        (await Direct(cell).GetFromJsonAsync<JsonElement>("/health")).GetProperty("version").GetString()!;

    public async Task<string[]> History(string cell) =>
        (await Direct(cell).GetFromJsonAsync<JsonElement>("/health")).GetProperty("history").EnumerateArray().Select(v => v.GetString()!).ToArray();

    public async ValueTask DisposeAsync()
    {
        await Router.DisposeAsync();
        foreach (var cell in Cells.Values)
            await cell.DisposeAsync();
    }
}
