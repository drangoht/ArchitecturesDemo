using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Mt.Api;

namespace Mt.Tests;

/// <summary>L'application complète : identification du locataire, quotas, back-office de la plateforme.</summary>
public class ApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _app.DisposeAsync().AsTask();

    /// <summary>Un client qui s'annonce par l'en-tête X-Locataire.</summary>
    private HttpClient Client(string? locataire = null)
    {
        var client = _app.CreateClient();
        if (locataire is not null)
            client.DefaultRequestHeaders.Add("X-Locataire", locataire);
        return client;
    }

    private async Task<decimal> Solde(string locataire, Guid compte) =>
        (await Client(locataire).GetFromJsonAsync<JsonElement>($"/comptes/{compte}")).GetProperty("solde").GetDecimal();

    private async Task<JsonElement> Stockage() => await Client().GetFromJsonAsync<JsonElement>("/plateforme/stockage");

    private static IEnumerable<string> LocatairesDe(JsonElement stockage, string nomBase) =>
        stockage.EnumerateArray().Where(b => b.GetProperty("base").GetString() == nomBase)
            .SelectMany(b => b.GetProperty("locataires").EnumerateArray().Select(l => l.GetProperty("locataire").GetString()!));

    [Fact]
    public async Task La_meme_URL_donne_une_reponse_differente_selon_le_locataire()
    {
        Assert.Equal(1_000m, await Solde("alpha", DonneesDeDemo.Alice));
        Assert.Equal(3_000m, await Solde("beta", DonneesDeDemo.Alice));
        Assert.Equal(25_000m, await Solde("gamma", DonneesDeDemo.Alice));
    }

    [Fact]
    public async Task Le_locataire_se_reconnait_aussi_au_sous_domaine()
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://beta.localhost") });

        var reponse = await client.GetAsync($"/comptes/{DonneesDeDemo.Alice}");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("beta", reponse.Headers.GetValues("X-Locataire").Single());
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.BadRequest)]          // aucun locataire
    [InlineData("inconnue", null, HttpStatusCode.NotFound)]      // locataire absent du catalogue
    [InlineData("alpha", "http://beta.localhost", HttpStatusCode.BadRequest)]  // en-tête et sous-domaine se contredisent
    [InlineData("beta", "http://beta.localhost", HttpStatusCode.OK)]           // concordants : accepté
    public async Task Une_requete_sans_locataire_clair_n_atteint_pas_le_metier(string? enTete, string? adresse, HttpStatusCode attendu)
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(adresse ?? "http://localhost") });
        if (enTete is not null)
            client.DefaultRequestHeaders.Add("X-Locataire", enTete);

        Assert.Equal(attendu, (await client.GetAsync("/comptes")).StatusCode);
    }

    [Fact]
    public async Task Le_compte_d_une_autre_banque_repond_404_comme_un_compte_inexistant()
    {
        var reponse = await Client("alpha").PostAsJsonAsync("/comptes", new { titulaire = "Zoe", depotInitial = 100 });
        var url = reponse.Headers.Location!.ToString();

        Assert.Equal(HttpStatusCode.OK, (await Client("alpha").GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client("beta").GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Les_regles_du_virement_dependent_de_la_banque()
    {
        var virement = new { source = DonneesDeDemo.Alice, cible = DonneesDeDemo.Bob, montant = 1_500 };

        Assert.Equal(HttpStatusCode.BadRequest, (await Client("alpha").PostAsJsonAsync("/virements", virement)).StatusCode);  // plafond 1 000 €
        var beta = await (await Client("beta").PostAsJsonAsync("/virements", virement)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.5m, beta.GetProperty("frais").GetDecimal());
    }

    [Fact]
    public async Task Un_locataire_suspendu_est_refuse_puis_retrouve_ses_donnees()
    {
        await Client().PostAsync("/plateforme/locataires/alpha/suspendre", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client("alpha").GetAsync("/comptes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client("beta").GetAsync("/comptes")).StatusCode);

        await Client().PostAsync("/plateforme/locataires/alpha/reactiver", null);
        Assert.Equal(1_000m, await Solde("alpha", DonneesDeDemo.Alice));
    }

    [Fact]
    public async Task Un_voisin_bruyant_epuise_son_quota_sans_gener_les_autres()
    {
        var alpha = Client("alpha");
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/locataire")).StatusCode);

        var refus = await alpha.GetAsync("/locataire");

        Assert.Equal(HttpStatusCode.TooManyRequests, refus.StatusCode);
        Assert.Contains("60 requêtes par minute", await refus.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await Client("beta").GetAsync("/locataire")).StatusCode);
    }

    [Fact]
    public async Task Un_nouveau_client_est_operationnel_des_son_inscription()
    {
        var definition = new
        {
            id = "delta", nom = "Banque Delta", plan = "Premium",
            regles = new { plafondVirement = 2_000, fraisVirement = 1, fraisMensuels = 0, comptesMax = 50 }
        };

        var inscription = await Client().PostAsJsonAsync("/plateforme/locataires", definition);
        var ouverture = await Client("delta").PostAsJsonAsync("/comptes", new { titulaire = "Zoe", depotInitial = 100 });

        Assert.Equal(HttpStatusCode.Created, inscription.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ouverture.StatusCode);
        Assert.Contains("delta", LocatairesDe(await Stockage(), "banque-delta"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsJsonAsync("/plateforme/locataires", definition)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Client().PostAsJsonAsync("/plateforme/locataires", definition with { id = "Pas Valide!" })).StatusCode);
    }

    [Fact]
    public async Task Le_depart_d_un_client_efface_ses_donnees_et_seulement_les_siennes()
    {
        var bilan = await (await Client().DeleteAsync("/plateforme/locataires/alpha")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, bilan.GetProperty("comptes").GetInt32());
        Assert.Equal(["beta"], LocatairesDe(await Stockage(), "banque-partagee"));
        Assert.Equal(HttpStatusCode.NotFound, (await Client("alpha").GetAsync("/comptes")).StatusCode);
        Assert.Equal(3_000m, await Solde("beta", DonneesDeDemo.Alice));
    }

    [Fact]
    public async Task Passer_en_premium_demenage_les_donnees_dans_une_base_dediee()
    {
        await Client("alpha").PostAsJsonAsync($"/comptes/{DonneesDeDemo.Alice}/retrait", new { montant = 100 });

        var reponse = await Client().PostAsJsonAsync("/plateforme/locataires/alpha/formule", new { plan = "Premium" });
        var bilan = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("banque-alpha", bilan.GetProperty("vers").GetString());
        Assert.Equal(2, bilan.GetProperty("comptes").GetInt32());
        Assert.Equal(900m, await Solde("alpha", DonneesDeDemo.Alice));
        Assert.Equal(2, (await Client("alpha").GetFromJsonAsync<JsonElement>($"/comptes/{DonneesDeDemo.Alice}/operations")).GetArrayLength());  // dépôt initial + retrait
        var stockage = await Stockage();
        Assert.DoesNotContain("alpha", LocatairesDe(stockage, "banque-partagee"));
        Assert.Equal(["alpha"], LocatairesDe(stockage, "banque-alpha"));
        var quotas = (await Client("alpha").GetFromJsonAsync<JsonElement>("/locataire")).GetProperty("quotas");
        Assert.Equal(600, quotas.GetProperty("requetesParMinute").GetInt32());
    }

    [Fact]
    public async Task La_tache_de_frais_mensuels_travaille_locataire_par_locataire()
    {
        await Client().PostAsync("/plateforme/locataires/beta/suspendre", null);

        var bilans = await (await Client().PostAsJsonAsync("/plateforme/taches/frais-mensuels", new { mois = "2026-10" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        await Client().PostAsJsonAsync("/plateforme/taches/frais-mensuels", new { mois = "2026-10" });  // rejouée : sans effet
        await Client().PostAsync("/plateforme/locataires/beta/reactiver", null);

        Assert.Equal(998m, await Solde("alpha", DonneesDeDemo.Alice));    // 2 € chez Alpha
        Assert.Equal(3_000m, await Solde("beta", DonneesDeDemo.Alice));   // Bêta suspendue : ignorée
        Assert.Equal(24_990m, await Solde("gamma", DonneesDeDemo.Alice)); // 10 € chez Gamma, dans sa propre base
        Assert.Contains(bilans.EnumerateArray(), b => b.GetProperty("locataire").GetString() == "beta"
                                                      && b.GetProperty("remarque").GetString() == "suspendu : ignoré");
    }
}
