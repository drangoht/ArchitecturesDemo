using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Espace.Banque;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Espace.Tests;

/// <summary>L'application complète, sans élasticité automatique ni latences (les tests la pilotent).</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseSetting("Elasticite:PeriodeEvaluationMs", "0")
        .UseSetting("Plateforme:LatenceReplicationMs", "0")
        .UseSetting("Plateforme:LatenceBaseMs", "0")
        .UseSetting("Plateforme:TempsDeTraitementMs", "0"));

    private HttpClient _client = null!;
    private static readonly string Alice = DonneesDeDemo.Alice.ToString();

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private async Task<JsonElement> Get(string url) => await _client.GetFromJsonAsync<JsonElement>(url);

    [Fact]
    public async Task Chaque_reponse_dit_quelle_unite_l_a_traitee()
    {
        var reponse = await _client.GetAsync($"/comptes/{Alice}");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Matches("^u[12]$", reponse.Headers.GetValues("X-Unite").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/comptes/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 5_000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/comptes", new { titulaire = "Zoe", depotInitial = 10 })).StatusCode);
    }

    [Fact]
    public async Task Base_en_panne_les_requetes_passent_mais_un_demarrage_a_froid_est_impossible()
    {
        await _client.PostAsync("/grille/base/panne", null);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 100 })).StatusCode);
        Assert.Equal(1, (await Get("/grille")).GetProperty("pompeEnAttente").GetInt32());

        await _client.PostAsync("/grille/panne-totale", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync($"/comptes/{Alice}")).StatusCode);

        await _client.PostAsync("/grille/base/retablir", null);
        Assert.Equal(900m, (await Get($"/comptes/{Alice}")).GetProperty("solde").GetDecimal());
        Assert.Equal(2, (await Get("/grille")).GetProperty("demarragesAFroid").GetInt32());
    }

    [Fact]
    public async Task La_demo_des_retraits_concurrents_en_affinite_ne_perd_rien()
    {
        await _client.PostAsync("/grille/unites", null);

        var bilan = await (await _client.PostAsJsonAsync("/demo/retraits-concurrents", new { compte = Alice, nombre = 300, montant = 3 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(333 - 33, bilan.GetProperty("acceptes").GetInt32());
        Assert.Equal(100m, bilan.GetProperty("soldeAttendu").GetDecimal());
        Assert.Equal([100m], bilan.GetProperty("soldeDansLaGrille").EnumerateArray().Select(s => s.GetDecimal()));
        Assert.Equal(100m, bilan.GetProperty("soldeDansLaBase").GetDecimal());
        Assert.Equal(0, bilan.GetProperty("misesAJourPerdues").GetInt32());
        Assert.Single(bilan.GetProperty("unitesSollicitees").EnumerateArray());
    }

    [Fact]
    public async Task On_peut_ajouter_arreter_et_faire_tomber_des_unites()
    {
        Assert.Equal("u3", (await (await _client.PostAsync("/grille/unites", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unite").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/grille/unites/u1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync("/grille/unites/u2/panne", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/grille/unites/u9")).StatusCode);

        var unites = (await Get("/grille")).GetProperty("unites").EnumerateArray().Select(u => u.GetProperty("id").GetString());
        Assert.Equal(["u3"], unites);
        Assert.Equal(1_000m, (await Get($"/comptes/{Alice}")).GetProperty("solde").GetDecimal());
        Assert.Contains((await Get("/grille/journal")).EnumerateArray(), e => e.GetString()!.Contains("u2 EN PANNE"));
    }

    [Fact]
    public async Task La_comparaison_montre_la_meme_version_partout_au_repos()
    {
        await _client.PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 100 });
        for (var i = 0; i < 200 && await Get("/grille") is var g
             && g.GetProperty("replicationEnAttente").GetInt32() + g.GetProperty("pompeEnAttente").GetInt32() > 0; i++)
            await Task.Delay(5);  // attendre le repos : réplication livrée, pompe vidée

        var comparaison = await Get($"/grille/comparaison/{Alice}");

        var versions = comparaison.GetProperty("repliques").EnumerateArray().Select(r => r.GetProperty("version").GetString())
            .Append(comparaison.GetProperty("base").GetProperty("version").GetString());
        Assert.Single(versions.Distinct());
        Assert.StartsWith("v2@u", versions.First());
    }
}
