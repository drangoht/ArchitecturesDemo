using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Choreo.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Choreo.Tests;

/// <summary>L'application complète. Expiration automatique désactivée : les tests ne dépendent pas d'une minuterie.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app =
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Banque:PeriodeExpirationMs", "0"));
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _app.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    private Task<HttpResponseMessage> Virer(Guid source, Guid cible, decimal montant, bool attendre = true) =>
        _client.PostAsJsonAsync($"/virements{(attendre ? "?attendre=true" : "")}", new { source, cible, montant });

    private async Task<JsonElement> Get(string url) => await _client.GetFromJsonAsync<JsonElement>(url);

    /// <summary>Le traitement est asynchrone : on interroge jusqu'à ce que la condition soit vraie (5 s au plus).</summary>
    private async Task<JsonElement> Attendre(string url, Func<JsonElement, bool> condition)
    {
        for (var i = 0; ; i++)
        {
            var reponse = await _client.GetAsync(url);
            if (reponse.IsSuccessStatusCode && await reponse.Content.ReadFromJsonAsync<JsonElement>() is var json && condition(json))
                return json;
            Assert.True(i < 500, $"{url} : condition jamais remplie");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Demander_un_virement_repond_202_et_la_suite_se_fait_sans_l_appelant()
    {
        var reponse = await Virer(DonneesDeDemo.Alice, DonneesDeDemo.Carla, 100m, attendre: false);

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        var virement = await Attendre(reponse.Headers.Location!.ToString(), v => v.GetProperty("historique").GetArrayLength() == 6);

        Assert.Equal("Regle", virement.GetProperty("etat").GetProperty("statut").GetString());
        Assert.Equal(10, (await Get($"/fidelite/{DonneesDeDemo.Alice}")).GetProperty("points").GetInt32());
        Assert.Single((await Get("/notifications")).EnumerateArray());
    }

    [Fact]
    public async Task L_analyste_decide_par_l_API()
    {
        var id = (await (await Virer(DonneesDeDemo.Alice, DonneesDeDemo.Bob, 15_000m)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        Assert.Equal([id], (await Get("/conformite/en-attente")).EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsJsonAsync($"/conformite/{id}/decision", new { approuve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync($"/conformite/{id}/decision", new { approuve = true })).StatusCode);
    }

    [Fact]
    public async Task Un_service_en_panne_se_voit_aux_lettres_mortes_et_aux_virements_bloques()
    {
        await _client.PostAsJsonAsync("/chaos", new { changeEnPanne = true });

        var id = (await (await Virer(DonneesDeDemo.Alice, DonneesDeDemo.Bob, 200m, attendre: false)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var lettresMortes = await Attendre("/bus/lettres-mortes", l => l.GetArrayLength() > 0);
        Assert.Equal(("Change", id), (lettresMortes[0].GetProperty("abonne").GetString(), lettresMortes[0].GetProperty("virement").GetString()));
        Assert.Equal("EnCours", (await Get($"/virements/{id}")).GetProperty("etat").GetProperty("statut").GetString());
        Assert.Contains((await Get("/virements/bloques?delaiSecondes=0")).EnumerateArray(), v => v.GetProperty("id").GetString() == id);
        Assert.Equal(200m, (await Get($"/comptes/{DonneesDeDemo.Alice}")).GetProperty("reserve").GetDecimal());
    }

    [Fact]
    public async Task Le_bus_montre_qui_ecoute_quoi()
    {
        var abonnes = (await Get("/bus")).EnumerateArray().ToDictionary(a => a.GetProperty("abonne").GetString()!,
            a => a.GetProperty("ecoute").EnumerateArray().Select(e => e.GetString()).ToList());

        Assert.Equal(["ChangeEchoue", "ConformiteRefusee", "DevisesConverties", "VirementDemande"], abonnes["Comptes"]);
        Assert.Equal(["VirementRegle"], abonnes["Fidélité"]);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/virements/inconnu")).StatusCode);
    }
}
