using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Saga.Api;

namespace Saga.Tests;

public class OrchestrateurEnTest : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Saga:PauseEntreEtapesMs", "0");
        builder.UseSetting("Saga:AttenteInitialeMs", "20");
    }
}

/// <summary>L'orchestrateur réel, avec sa boucle en tâche de fond.</summary>
public class ApiTests
{
    private static async Task<JsonElement> AttendreStatut(HttpClient client, string url, params string[] statuts)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        JsonElement saga;
        do
        {
            saga = await client.GetFromJsonAsync<JsonElement>(url);
            if (statuts.Contains(saga.GetProperty("statut").GetString()))
                return saga;
            await Task.Delay(30);
        } while (DateTime.UtcNow < limite);
        Assert.Fail($"Statut attendu {string.Join("/", statuts)}, obtenu : {saga}");
        return saga;
    }

    [Fact]
    public async Task Une_saga_demarree_par_HTTP_va_jusqu_au_bout()
    {
        await using var app = new OrchestrateurEnTest();
        var client = app.CreateClient();

        var reponse = await client.PostAsJsonAsync("/virements", new { source = DonneesDeDemo.Alice, cible = DonneesDeDemo.Carla, montant = 100m });

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        var saga = await AttendreStatut(client, reponse.Headers.Location!.ToString(), "Terminee");
        Assert.All(saga.GetProperty("etapes").EnumerateArray(), e => Assert.Equal("faite", e.GetProperty("etat").GetString()));
    }

    [Fact]
    public async Task La_decision_de_l_analyste_arrive_par_HTTP()
    {
        await using var app = new OrchestrateurEnTest();
        var client = app.CreateClient();
        var reponse = await client.PostAsJsonAsync("/virements", new { source = DonneesDeDemo.Alice, cible = DonneesDeDemo.Bob, montant = 12_000m });
        var url = reponse.Headers.Location!.ToString();
        await AttendreStatut(client, url, "EnAttente");

        var decision = await client.PostAsJsonAsync($"{url}/conformite", new { approuve = false, commentaire = "origine des fonds non justifiée" });

        Assert.Equal(HttpStatusCode.NoContent, decision.StatusCode);
        var saga = await AttendreStatut(client, url, "Annulee");
        Assert.Contains("origine des fonds", saga.GetProperty("raison").GetString());
    }

    [Fact]
    public async Task Un_orchestrateur_arrete_puis_relance_reprend_la_saga()
    {
        await using var app = new OrchestrateurEnTest();
        var client = app.CreateClient();
        await client.PostAsync("/admin/orchestrateur/arreter", null);

        var reponse = await client.PostAsJsonAsync("/virements", new { source = DonneesDeDemo.Alice, cible = DonneesDeDemo.Bob, montant = 50m });
        var url = reponse.Headers.Location!.ToString();
        await Task.Delay(300);
        Assert.Equal("EnCours", (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("statut").GetString());

        await client.PostAsync("/admin/orchestrateur/relancer", null);

        await AttendreStatut(client, url, "Terminee");
    }
}
