using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Ddd.Conformite;
using Ddd.TenueDeCompte.Contrats;
using Ddd.TenueDeCompte.Domaine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ddd.Tests;

public class BanqueApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("NomBase", Guid.NewGuid().ToString());
}

public class ApiTests
{
    private static async Task<JsonElement> Compte(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/comptes/{id}");

    private static async Task<string> Iban(HttpClient client, Guid id) => (await Compte(client, id)).GetProperty("iban").GetString()!;

    private static Task<HttpResponseMessage> Virer(HttpClient client, Guid source, string iban, decimal montant) =>
        client.PostAsJsonAsync($"/comptes/{source}/virements", new { ibanBeneficiaire = iban, montant });

    [Fact]
    public async Task Trois_virements_rapproches_declenchent_un_blocage_par_la_conformite()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();
        var ibanAlice = await Iban(client, DonneesDeDemo.Alice);

        for (var i = 0; i < 3; i++)
            (await Virer(client, DonneesDeDemo.Bob, ibanAlice, 10m)).EnsureSuccessStatusCode();
        var quatrieme = await Virer(client, DonneesDeDemo.Bob, ibanAlice, 10m);

        Assert.Equal(HttpStatusCode.BadRequest, quatrieme.StatusCode);
        var bob = await Compte(client, DonneesDeDemo.Bob);
        Assert.Equal("Bloque", bob.GetProperty("statut").GetString());
        Assert.Contains("fractionnement", bob.GetProperty("motifBlocage").GetString());
        var alertes = await client.GetFromJsonAsync<JsonElement>("/conformite/alertes");
        Assert.Equal("Critique", Assert.Single(alertes.EnumerateArray()).GetProperty("niveau").GetString());
    }

    [Fact]
    public async Task Un_virement_en_euros_vers_un_compte_en_dollars_est_converti()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        (await Virer(client, DonneesDeDemo.Alice, await Iban(client, DonneesDeDemo.Carla), 100m)).EnsureSuccessStatusCode();

        var carla = await Compte(client, DonneesDeDemo.Carla);
        Assert.Equal(1_108.32m, carla.GetProperty("solde").GetDecimal());
        Assert.Equal("USD", carla.GetProperty("devise").GetString());
    }

    [Fact]
    public async Task Un_gros_virement_est_signale_sans_bloquer_le_compte()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        (await Virer(client, DonneesDeDemo.Alice, await Iban(client, DonneesDeDemo.Bob), 12_000m)).EnsureSuccessStatusCode();

        var alertes = await client.GetFromJsonAsync<JsonElement>("/conformite/alertes");
        Assert.Equal("Elevee", Assert.Single(alertes.EnumerateArray()).GetProperty("niveau").GetString());
        Assert.Equal("Actif", (await Compte(client, DonneesDeDemo.Alice)).GetProperty("statut").GetString());
    }

    [Fact]
    public async Task Un_iban_invalide_est_refuse_avec_un_message_metier()
    {
        await using var app = new BanqueApplication();

        var reponse = await Virer(app.CreateClient(), DonneesDeDemo.Alice, "FR00 1234", 10m);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("IBAN invalide", await reponse.Content.ReadAsStringAsync());
    }

    // ─── Frontières des contextes délimités ───

    private static string[] DependancesInternes(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Ddd.")).ToArray();

    [Fact]
    public void Le_domaine_ne_depend_de_rien()
    {
        Assert.Empty(DependancesInternes(typeof(RegleMetierException).Assembly));
    }

    [Fact]
    public void La_conformite_ne_connait_que_le_langage_publie_de_la_tenue_de_compte()
    {
        Assert.Equal(["Ddd.TenueDeCompte.Contrats"], DependancesInternes(typeof(DossierDeVigilance).Assembly));
    }

    [Fact]
    public void Le_langage_publie_n_expose_aucun_type_du_domaine()
    {
        Assert.Empty(DependancesInternes(typeof(VirementEmis).Assembly));
    }
}
