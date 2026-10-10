using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Mk.Hote;

namespace Mk.Tests;

public class BanqueAvecPlugins(DateTime heureLocale) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Plugins:Dossier", ChargementTests.DossierPlugins());
        builder.UseSetting("Plugins:Surveiller", "false");
        builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new HorlogeReglable(heureLocale)));
    }
}

/// <summary>Le noyau et ses vrais plugins, à travers l'API.</summary>
public class ApiTests
{
    private static readonly DateTime Apres_midi = new(2026, 10, 12, 14, 0, 0);
    private static readonly DateTime Nuit = new(2026, 10, 12, 23, 30, 0);

    private static Task<HttpResponseMessage> Virer(HttpClient client, Guid cible, decimal montant) =>
        client.PostAsJsonAsync("/virements", new { source = DonneesDeDemo.Alice, cible, montant });

    [Fact]
    public async Task Le_plugin_de_frais_s_applique_aux_virements()
    {
        await using var app = new BanqueAvecPlugins(Apres_midi);

        var reponse = await Virer(app.CreateClient(), DonneesDeDemo.Bob, 300m);

        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((0.30m, 699.70m), (corps.GetProperty("frais").GetDecimal(), corps.GetProperty("solde").GetDecimal()));
    }

    [Fact]
    public async Task Le_plugin_liste_noire_refuse_les_virements_vers_un_compte_liste()
    {
        await using var app = new BanqueAvecPlugins(Apres_midi);

        var reponse = await Virer(app.CreateClient(), DonneesDeDemo.Carla, 10m);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("[ListeNoire]", await reponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(14, HttpStatusCode.OK)]
    [InlineData(23, HttpStatusCode.BadRequest)]
    public async Task Le_plugin_plafond_nocturne_depend_de_l_heure(int heure, HttpStatusCode attendu)
    {
        await using var app = new BanqueAvecPlugins(new DateTime(2026, 10, 12, heure, 30, 0));

        var reponse = await app.CreateClient().PostAsJsonAsync($"/comptes/{DonneesDeDemo.Alice}/retrait", new { montant = 250m });

        Assert.Equal(attendu, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_formats_de_releve_viennent_des_plugins()
    {
        await using var app = new BanqueAvecPlugins(Apres_midi);
        var client = app.CreateClient();

        var csv = await client.GetStringAsync($"/comptes/{DonneesDeDemo.Alice}/releve?format=csv");
        var qif = await client.GetStringAsync($"/comptes/{DonneesDeDemo.Alice}/releve?format=qif");
        var inconnu = await client.GetAsync($"/comptes/{DonneesDeDemo.Alice}/releve?format=pdf");

        Assert.StartsWith("date;libelle;montant;solde", csv);
        Assert.StartsWith("!Type:Bank", qif);
        Assert.Contains("\"csv\"", await inconnu.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Desactiver_un_plugin_change_le_comportement_sans_redemarrer()
    {
        await using var app = new BanqueAvecPlugins(Nuit);
        var client = app.CreateClient();

        await client.PostAsync("/admin/plugins/FraisDeVirement/desactiver", null);
        var reponse = await Virer(client, DonneesDeDemo.Bob, 300m);

        Assert.Equal(0m, (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("frais").GetDecimal());
    }
}
