using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Cell.Tests;

/// <summary>
/// Le système complet en mémoire : quatre cellules (c4 en réserve, non enregistrée au départ) et le routeur.
/// Le routeur joint chaque cellule par son serveur de test ; les cellules joignent le routeur de la même façon.
/// </summary>
public sealed class Systeme : IAsyncDisposable
{
    public const string Alice = "11111111-1111-1111-1111-111111111111";
    public const string Bob = "22222222-2222-2222-2222-222222222222";
    public const string David = "55555555-5555-5555-5555-555555555555";

    public Dictionary<string, WebApplicationFactory<Cell.Cellule.Marqueur>> Cellules { get; } = [];
    public WebApplicationFactory<Cell.Routeur.Marqueur> Routeur { get; }

    public Systeme()
    {
        foreach (var id in new[] { "c1", "c2", "c3", "c4" })
            Cellules[id] = new WebApplicationFactory<Cell.Cellule.Marqueur>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Cellule:Id", id);
                b.ConfigureTestServices(s => s.AddHttpClient("routeur").ConfigurePrimaryHttpMessageHandler(() => Routeur!.Server.CreateHandler()));
            });

        Routeur = new WebApplicationFactory<Cell.Routeur.Marqueur>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Deploiement:ObservationMs", "0");
            b.ConfigureTestServices(s =>
            {
                foreach (var (id, cellule) in Cellules)
                    s.AddHttpClient($"cellule-{id}").ConfigurePrimaryHttpMessageHandler(() => cellule.Server.CreateHandler());
            });
        });

        // Démarrer le routeur tout de suite : il place les clients de démo dans les cellules, comme au lancement réel.
        _ = Routeur.Server;
    }

    /// <summary>Un client de la banque : il ne connaît que le routeur.</summary>
    public HttpClient Client() => Routeur.CreateClient();

    public HttpClient Direct(string cellule) => Cellules[cellule].CreateClient();

    public async Task<decimal> Solde(string client) =>
        (await Client().GetFromJsonAsync<JsonElement>($"/clients/{client}/comptes"))[0].GetProperty("solde").GetDecimal();

    public async Task<JsonElement> Placement() => await Client().GetFromJsonAsync<JsonElement>("/plateforme/placement");

    public async Task MettreEnPanne(string cellule, bool enPanne = true) =>
        (await Direct(cellule).PutAsJsonAsync("/admin/panne", new { enPanne })).EnsureSuccessStatusCode();

    public async Task<string> Version(string cellule) =>
        (await Direct(cellule).GetFromJsonAsync<JsonElement>("/sante")).GetProperty("version").GetString()!;

    public async Task<string[]> Historique(string cellule) =>
        (await Direct(cellule).GetFromJsonAsync<JsonElement>("/sante")).GetProperty("historique").EnumerateArray().Select(v => v.GetString()!).ToArray();

    public async ValueTask DisposeAsync()
    {
        await Routeur.DisposeAsync();
        foreach (var cellule in Cellules.Values)
            await cellule.DisposeAsync();
    }
}
