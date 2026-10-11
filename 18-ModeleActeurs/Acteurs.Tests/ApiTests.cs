using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acteurs.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Acteurs.Tests;

/// <summary>L'application complète : routes → messages → acteurs, et ce que le runtime signale → codes HTTP.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;

    private static readonly string Alice = DonneesDeDemo.Alice.ToString(), Bob = DonneesDeDemo.Bob.ToString();

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private async Task<JsonElement> Get(string url) => await _client.GetFromJsonAsync<JsonElement>(url);

    private async Task<decimal> Solde(string compte) => (await Get($"/comptes/{compte}")).GetProperty("solde").GetDecimal();

    private Task<HttpResponseMessage> Retrait(string compte, decimal montant) =>
        _client.PostAsJsonAsync($"/comptes/{compte}/retrait", new { montant });

    private Task Pieger(string acteur, string etape, int fois = 1) =>
        _client.PostAsJsonAsync("/systeme/pannes", new { acteur, etape, fois });

    [Fact]
    public async Task Les_reponses_des_acteurs_deviennent_des_codes_HTTP()
    {
        Assert.Equal(HttpStatusCode.OK, (await Retrait(Alice, 100)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Retrait(Alice, 5_000)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/comptes/{Guid.NewGuid()}")).StatusCode);

        var ouverture = await _client.PostAsJsonAsync("/comptes", new { titulaire = "Zoe", depotInitial = 50 });
        Assert.Equal(HttpStatusCode.Created, ouverture.StatusCode);
        Assert.Equal(50m, (await Get(ouverture.Headers.Location!.ToString())).GetProperty("solde").GetDecimal());
    }

    [Fact]
    public async Task Une_panne_repond_500_et_le_compte_suivant_repond_normalement()
    {
        await Pieger($"compte/{Alice}", "AvantSauvegarde");

        var reponse = await Retrait(Alice, 100);

        Assert.Equal(HttpStatusCode.InternalServerError, reponse.StatusCode);
        Assert.Equal($"compte/{Alice}", (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("acteur").GetString());
        Assert.Equal(1_000m, await Solde(Alice));
        Assert.Equal(1, (await Get("/systeme")).GetProperty("redemarrages").GetInt32());
        Assert.Contains("redémarré", (await Get("/systeme/journal"))[0].GetString());
    }

    [Fact]
    public async Task Des_pannes_repetees_mettent_l_acteur_en_quarantaine()
    {
        await Pieger($"compte/{Bob}", "Reception", fois: 4);  // stratégie de l'API : 3 redémarrages par minute

        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.InternalServerError, (await _client.GetAsync($"/comptes/{Bob}")).StatusCode);
        for (var i = 0; i < 50 && (await Get("/systeme")).GetProperty("arrets").GetInt32() == 0; i++)
            await Task.Delay(10);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync($"/comptes/{Bob}")).StatusCode);
        Assert.Equal(1_000m, await Solde(Alice));  // les autres acteurs ne sont pas concernés
    }

    [Fact]
    public async Task Une_lenteur_repond_504_sans_bloquer_les_autres_comptes()
    {
        var client = _app.WithWebHostBuilder(b => b.UseSetting("Acteurs:DelaiDemandeMs", "300")).CreateClient();
        await client.PostAsJsonAsync("/systeme/pannes", new { acteur = $"compte/{Bob}", etape = "Reception", nature = "Lenteur", latenceMs = 1_000 });

        var lente = client.GetAsync($"/comptes/{Bob}");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/comptes/{Alice}")).StatusCode);

        Assert.Equal(HttpStatusCode.GatewayTimeout, (await lente).StatusCode);
        await Task.Delay(1_000);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/comptes/{Bob}")).StatusCode);  // la lenteur passée, Bob répond
    }

    [Fact]
    public async Task La_demo_des_retraits_simultanes_ne_laisse_jamais_le_solde_negatif()
    {
        var bilan = await (await _client.PostAsJsonAsync("/demo/retraits-simultanes", new { compte = Alice, nombre = 1_000, montant = 3 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(333, bilan.GetProperty("acceptes").GetInt32());
        Assert.Equal(1m, bilan.GetProperty("soldeFinal").GetDecimal());
        Assert.True(bilan.GetProperty("soldeMinimumObserve").GetDecimal() >= 0);
    }

    [Fact]
    public async Task La_demo_des_virements_croises_conserve_l_argent()
    {
        var bilan = await (await _client.PostAsJsonAsync("/demo/virements-croises", new { nombre = 100, montant = 10 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(200, bilan.GetProperty("virements").GetInt32());
        Assert.Equal(1_500m, bilan.GetProperty("totalAvant").GetDecimal());
        Assert.Equal(1_500m, bilan.GetProperty("totalApres").GetDecimal());
    }

    [Fact]
    public async Task Un_virement_identifie_peut_etre_rejoue_sans_risque()
    {
        var virement = new { id = "facture-42", source = Alice, cible = Bob, montant = 100 };

        var premier = await (await _client.PostAsJsonAsync("/virements", virement)).Content.ReadFromJsonAsync<JsonElement>();
        await _client.PostAsJsonAsync("/virements", virement);

        Assert.Equal("Termine", premier.GetProperty("statut").GetString());
        Assert.Equal(900m, await Solde(Alice));
        Assert.Equal("Termine", (await Get("/virements/facture-42")).GetProperty("statut").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/virements", virement with { id = "pas valide !" })).StatusCode);
    }

    [Fact]
    public async Task La_passivation_vide_la_memoire_mais_pas_le_stockage()
    {
        await Retrait(Alice, 100);

        var passives = (await (await _client.PostAsync("/systeme/passivation", null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("passives").GetInt32();

        Assert.Equal(2, passives);
        Assert.Equal(0, (await Get("/systeme")).GetProperty("actifs").GetInt32());
        Assert.Contains($"compte/{Alice}", (await Get("/systeme/stockage?type=compte")).EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(900m, await Solde(Alice));
        Assert.Equal(1, (await Get("/systeme")).GetProperty("actifs").GetInt32());
    }
}
