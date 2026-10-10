using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cqrs.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cqrs.Tests;

public class BanqueApplication(int delaiProjectionMs) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("NomBase", Guid.NewGuid().ToString());
        builder.UseSetting("Projections:DelaiMs", delaiProjectionMs.ToString());
    }
}

public class ApiTests
{
    private static readonly Guid Alice = DonneesDeDemo.Alice;
    private static readonly Guid Bob = DonneesDeDemo.Bob;

    private static async Task<decimal?> Solde(HttpClient client, Guid id)
    {
        var reponse = await client.GetAsync($"/comptes/{id}");
        if (reponse.StatusCode == HttpStatusCode.NotFound)
            return null;
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solde").GetDecimal();
    }

    private static async Task AttendreSolde(HttpClient client, Guid id, decimal attendu)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (await Solde(client, id) != attendu && DateTime.UtcNow < limite)
            await Task.Delay(50);
        Assert.Equal(attendu, await Solde(client, id));
    }

    [Fact]
    public async Task Une_commande_est_acceptee_puis_visible_dans_les_vues()
    {
        await using var app = new BanqueApplication(delaiProjectionMs: 0);
        var client = app.CreateClient();
        await AttendreSolde(client, Bob, 500m);

        var reponse = await client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 250m });

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        await AttendreSolde(client, Bob, 750m);
        var operations = await client.GetFromJsonAsync<JsonElement>($"/comptes/{Bob}/operations");
        Assert.Equal("Virement reçu de Alice", operations[0].GetProperty("libelle").GetString());
        var tableau = await client.GetFromJsonAsync<JsonElement>("/tableau-de-bord");
        Assert.Equal(1, tableau.GetProperty("nombreVirements").GetInt32());
    }

    [Fact]
    public async Task La_lecture_est_coherente_a_terme()
    {
        await using var app = new BanqueApplication(delaiProjectionMs: 1500);
        var client = app.CreateClient();
        await AttendreSolde(client, Alice, 1000m);

        await client.PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 100m });

        // Juste après la commande, la vue n'est pas encore à jour…
        Assert.Equal(1000m, await Solde(client, Alice));
        var etat = await client.GetFromJsonAsync<JsonElement>("/admin/projections");
        Assert.Equal(1, etat.GetProperty("retard").GetInt64());
        // …elle le devient dès que la projection est passée.
        await AttendreSolde(client, Alice, 900m);
    }

    [Fact]
    public async Task Une_commande_invalide_renvoie_toutes_les_erreurs_de_validation()
    {
        await using var app = new BanqueApplication(delaiProjectionMs: 0);

        var reponse = await app.CreateClient().PostAsJsonAsync("/virements", new { source = Alice, cible = Alice, montant = 9000m });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, corps.GetProperty("erreurs").GetArrayLength());
    }
}
