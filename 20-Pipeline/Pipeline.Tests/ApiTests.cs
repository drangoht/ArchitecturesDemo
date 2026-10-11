using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Pipeline.Banque;

namespace Pipeline.Tests;

/// <summary>L'application complète : le fichier envoyé dans le corps de la requête, lu en flux.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private static readonly string Fichier = string.Join('\n',
        "reference;emetteur;beneficiaire;montant;devise",
        $"A-1;{DonneesDeDemo.Alice};{DonneesDeDemo.Bob};100;EUR",
        $"A-2;{DonneesDeDemo.Alice};{DonneesDeDemo.Bob};abc;EUR",
        $"A-3;{DonneesDeDemo.Bob};{DonneesDeDemo.Alice};50;EUR");

    private async Task<(HttpStatusCode Code, JsonElement Import)> Importer(string requete = "/imports")
    {
        var reponse = await _client.PostAsync(requete, new StringContent(Fichier));
        return (reponse.StatusCode, await reponse.Content.ReadFromJsonAsync<JsonElement>());
    }

    private async Task<decimal> Solde(string iban) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/comptes/{iban}")).GetProperty("solde").GetDecimal();

    [Fact]
    public async Task Un_import_execute_le_fichier_et_garde_son_compte_rendu()
    {
        var (code, import) = await Importer();

        Assert.Equal(HttpStatusCode.OK, code);
        var bilan = import.GetProperty("bilan");
        Assert.Equal((3, 2, 1), (bilan.GetProperty("lignes").GetInt32(), bilan.GetProperty("sorties").GetInt32(), bilan.GetProperty("rejets").GetInt32()));
        Assert.Equal(950m, await Solde(DonneesDeDemo.Alice));
        var id = import.GetProperty("id").GetInt32();
        Assert.Equal("Termine", (await _client.GetFromJsonAsync<JsonElement>($"/imports/{id}")).GetProperty("bilan").GetProperty("statut").GetString());
        Assert.Single((await _client.GetFromJsonAsync<JsonElement>("/imports")).EnumerateArray());
    }

    [Fact]
    public async Task Un_import_a_blanc_ne_touche_a_aucun_compte()
    {
        var (_, import) = await Importer("/imports?aBlanc=true");

        Assert.True(import.GetProperty("aBlanc").GetBoolean());
        Assert.Equal(2, import.GetProperty("bilan").GetProperty("sorties").GetInt32());
        Assert.Equal(1_000m, await Solde(DonneesDeDemo.Alice));
    }

    [Fact]
    public async Task Une_panne_repond_500_avec_le_compte_rendu()
    {
        var (code, import) = await Importer("/imports?panneALaReference=A-3");

        Assert.Equal(HttpStatusCode.InternalServerError, code);
        Assert.Equal("Echoue", import.GetProperty("bilan").GetProperty("statut").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/comptes/FR00INCONNU")).StatusCode);
    }

    [Fact]
    public async Task La_demo_de_l_import_massif_designe_le_goulot()
    {
        var bilan = await (await _client.PostAsJsonAsync("/demo/import-massif", new { lignes = 5_000, parallelismeFraude = 2 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Termine", bilan.GetProperty("statut").GetString());
        Assert.Equal(6, bilan.GetProperty("etages").GetArrayLength());
        Assert.Equal("Contrôler la fraude", bilan.GetProperty("goulot").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/demo/import-massif", new { lignes = 0 })).StatusCode);
    }
}
