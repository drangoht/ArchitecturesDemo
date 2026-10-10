using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Es.Api;
using Es.Domaine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Es.Tests;

public class BanqueApplication : WebApplicationFactory<Program>
{
    // Magasin en mémoire : chaque test part d'un historique neuf.
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Stockage:Fichier", "");
}

public class ApiTests
{
    private static readonly Guid Alice = DonneesDeDemo.Alice;
    private static readonly Guid Bob = DonneesDeDemo.Bob;

    private static async Task Retirer(HttpClient client, decimal montant, string motif) =>
        (await client.PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant, motif })).EnsureSuccessStatusCode();

    private static async Task<JsonElement> Attendre(HttpClient client, string url, Func<JsonElement, bool> condition)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        JsonElement json;
        do
        {
            json = await client.GetFromJsonAsync<JsonElement>(url);
            if (condition(json)) return json;
            await Task.Delay(50);
        } while (DateTime.UtcNow < limite);
        Assert.Fail($"{url} n'a pas atteint l'état attendu : {json}");
        return json;
    }

    [Fact]
    public async Task On_peut_revoir_l_etat_d_un_compte_a_n_importe_quelle_version()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();
        await Retirer(client, 100m, "Courses");
        await Retirer(client, 200m, "Loyer");

        var v2 = await client.GetFromJsonAsync<JsonElement>($"/comptes/{Alice}/etat?version=2");
        var v3 = await client.GetFromJsonAsync<JsonElement>($"/comptes/{Alice}/etat?version=3");

        Assert.Equal(900m, v2.GetProperty("solde").GetDecimal());
        Assert.Equal(700m, v3.GetProperty("solde").GetDecimal());
    }

    [Fact]
    public async Task Une_modification_basee_sur_une_version_perimee_est_refusee()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();
        await Retirer(client, 100m, "Courses");  // le compte passe en version 2

        using var requete = new HttpRequestMessage(HttpMethod.Post, $"/comptes/{Alice}/retrait")
        {
            Content = JsonContent.Create(new { montant = 50m })
        };
        requete.Headers.TryAddWithoutValidation("If-Match", "1");
        var reponse = await client.SendAsync(requete);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>($"/comptes/{Alice}/evenements")).GetArrayLength());
    }

    [Fact]
    public async Task Un_virement_ajoute_un_evenement_dans_chacun_des_deux_flux()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        (await client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 250m })).EnsureSuccessStatusCode();

        var bob = await client.GetFromJsonAsync<JsonElement>($"/comptes/{Bob}/evenements");
        Assert.Equal(["CompteOuvert", "VirementRecu"], bob.EnumerateArray().Select(e => e.GetProperty("type").GetString()));
        await Attendre(client, $"/comptes/{Bob}", r => r.GetProperty("solde").GetDecimal() == 750m);
    }

    [Fact]
    public async Task Les_projections_reconstruites_donnent_le_meme_resultat()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();
        await Retirer(client, 100m, "Courses");
        await Retirer(client, 300m, "Loyer");
        var avant = await Attendre(client, $"/comptes/{Alice}", r => r.GetProperty("version").GetInt32() == 3);

        var reconstruction = await client.PostAsync("/admin/projections/reconstruire", null);

        var rejoues = (await reconstruction.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evenementsRejoues").GetInt32();
        Assert.Equal(4, rejoues);  // 2 ouvertures + 2 retraits
        Assert.Equal(avant.ToString(), (await client.GetFromJsonAsync<JsonElement>($"/comptes/{Alice}")).ToString());
        var top = await client.GetFromJsonAsync<JsonElement>("/statistiques/plus-gros-retraits");
        Assert.Equal(["Loyer", "Courses"], top.EnumerateArray().Select(r => r.GetProperty("motif").GetString()));
    }

    [Fact]
    public void Le_domaine_ne_depend_de_rien()
    {
        var dependances = typeof(Compte).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Es."));
        Assert.Empty(dependances);
    }
}
