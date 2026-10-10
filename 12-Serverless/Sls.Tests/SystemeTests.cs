using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Sls.Fonctions;

namespace Sls.Tests;

public class PlateformeEnTest : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Plateforme:DemarrageAFroidMs", "30");
        builder.UseSetting("Plateforme:AttenteEntreTentativesMs", "30");
        builder.UseSetting("Plateforme:MsAvantRecyclage", "60000");
    }
}

/// <summary>Le système complet : fonctions déployées sur la plateforme, enchaînées par HTTP, files et stockage.</summary>
public class SystemeTests
{
    private static async Task<JsonElement> Attendre(HttpClient client, string url, Func<JsonElement, bool> condition)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
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
    public async Task Le_premier_appel_est_froid_et_le_second_chaud()
    {
        await using var plateforme = new PlateformeEnTest();
        var client = plateforme.CreateClient();

        var premier = await client.GetAsync($"/comptes/{DonneesDeDemo.Alice}");
        var second = await client.GetAsync($"/comptes/{DonneesDeDemo.Alice}");

        Assert.Equal("oui", premier.Headers.GetValues("X-Demarrage-A-Froid").Single());
        Assert.Equal("non", second.Headers.GetValues("X-Demarrage-A-Froid").Single());
    }

    [Fact]
    public async Task Un_virement_traverse_HTTP_puis_deux_files()
    {
        await using var plateforme = new PlateformeEnTest();
        var client = plateforme.CreateClient();

        var reponse = await client.PostAsJsonAsync("/virements", new { source = DonneesDeDemo.Alice, cible = DonneesDeDemo.Bob, montant = 250m });

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        await Attendre(client, reponse.Headers.Location!.ToString(), v => v.GetProperty("statut").GetString() == "Effectue");
        var notifications = await Attendre(client, $"/comptes/{DonneesDeDemo.Bob}/notifications", n => n.GetArrayLength() == 1);
        Assert.Contains("reçu de Alice", notifications[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Un_fichier_d_import_est_traite_par_plusieurs_instances_en_parallele()
    {
        await using var plateforme = new PlateformeEnTest();
        var client = plateforme.CreateClient();
        var csv = new StringBuilder("source;cible;montant\n");
        for (var i = 1; i <= 8; i++)
            csv.AppendLine($"{DonneesDeDemo.Carla};{(i % 2 == 0 ? DonneesDeDemo.Alice : DonneesDeDemo.Bob)};{i * 10}");

        await client.PostAsync("/imports", new StringContent(csv.ToString(), Encoding.UTF8, "text/plain"));

        await Attendre(client, $"/comptes/{DonneesDeDemo.Carla}", c => c.GetProperty("solde").GetDecimal() == 5_000m - 360m);
        var executer = (await client.GetFromJsonAsync<JsonElement>("/plateforme/fonctions")).EnumerateArray()
            .Single(f => f.GetProperty("nom").GetString() == "ExecuterVirement");
        Assert.True(executer.GetProperty("picInstances").GetInt32() > 1);
    }

    [Fact]
    public async Task Les_messages_en_echec_vont_en_file_poison_et_peuvent_etre_rejoues()
    {
        await using var plateforme = new PlateformeEnTest();
        var client = plateforme.CreateClient();
        await client.PutAsJsonAsync("/plateforme/reglages/Sms:EnPanne", new { valeur = "true" });

        await client.PostAsJsonAsync("/virements", new { source = DonneesDeDemo.Carla, cible = DonneesDeDemo.Alice, montant = 5m });
        await Attendre(client, "/plateforme/files", f => f.TryGetProperty("notifications-poison", out var p) && p.GetProperty("messages").GetInt32() == 2);

        await client.PutAsJsonAsync("/plateforme/reglages/Sms:EnPanne", new { valeur = "false" });
        await client.PostAsync("/plateforme/files/notifications-poison/rejouer", null);

        await Attendre(client, $"/comptes/{DonneesDeDemo.Alice}/notifications", n => n.GetArrayLength() == 1);
    }

    [Fact]
    public async Task La_facture_compte_chaque_invocation()
    {
        await using var plateforme = new PlateformeEnTest();
        var client = plateforme.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.GetAsync("/visites");

        var facture = await client.GetFromJsonAsync<JsonElement>("/plateforme/facture");

        Assert.Equal(5, facture.GetProperty("invocations").GetInt64());
        Assert.True(facture.GetProperty("totalUsd").GetDecimal() > 0);
    }
}
