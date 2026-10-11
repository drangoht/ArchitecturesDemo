using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Styles.Tests;

public class RestTests : IAsyncLifetime
{
    private readonly Systeme _systeme = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _systeme.DisposeAsync();

    [Fact]
    public async Task Chaque_ressource_a_son_adresse_et_donne_celles_des_ressources_liees()
    {
        var client = (await _systeme.Http.GetFromJsonAsync<JsonObject>("/clients/alice"))!;
        var comptes = (await _systeme.Http.GetFromJsonAsync<JsonArray>(client["_links"]!["comptes"]!.GetValue<string>()))!;
        var operations = (await _systeme.Http.GetFromJsonAsync<JsonArray>(comptes[0]!["_links"]!["operations"]!.GetValue<string>() + "?limite=2"))!;

        Assert.Equal(["10001", "10002", "10003"], comptes.Select(c => c!["numero"]!.GetValue<string>()));
        Assert.Equal(["Librairie", "Électricité"], operations.Select(o => o!["libelle"]!.GetValue<string>()));
    }

    [Fact]
    public async Task La_representation_d_un_compte_est_complete_et_la_meme_pour_tous_les_clients()
    {
        var compte = (await _systeme.Http.GetFromJsonAsync<JsonObject>("/comptes/10001"))!;

        // L'écran d'accueil n'a besoin que du libellé et du solde : il reçoit tout le reste aussi (sur-récupération).
        Assert.Equal(["numero", "libelle", "type", "iban", "solde", "devise", "dateOuverture", "plafondDecouvert", "titulaire", "_links"],
            compte.Select(p => p.Key));
    }

    [Fact]
    public async Task Un_virement_cree_une_ressource_201_dont_l_adresse_est_dans_Location()
    {
        var reponse = await _systeme.Http.PostAsJsonAsync("/virements", new { source = "10001", cible = "20001", montant = 40m, libelle = "Places" });

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.Equal("/virements/V001", reponse.Headers.Location!.ToString());
        var relu = (await _systeme.Http.GetFromJsonAsync<JsonObject>(reponse.Headers.Location))!;
        Assert.Equal((40m, 1586.05m), (relu["montant"]!.GetValue<decimal>(), relu["soldeSource"]!.GetValue<decimal>()));
    }

    [Theory]
    [InlineData("10001", "99999", 10, HttpStatusCode.NotFound, "COMPTE_INCONNU")]
    [InlineData("10001", "20001", -5, HttpStatusCode.BadRequest, "MONTANT_INVALIDE")]
    [InlineData("10001", "10001", 10, HttpStatusCode.BadRequest, "MEME_COMPTE")]
    [InlineData("10002", "10001", 9000, HttpStatusCode.UnprocessableEntity, "SOLDE_INSUFFISANT")]
    public async Task Les_erreurs_sont_des_statuts_HTTP_avec_un_corps_problem_json(string source, string cible, decimal montant,
        HttpStatusCode statut, string code)
    {
        var reponse = await _systeme.Http.PostAsJsonAsync("/virements", new { source, cible, montant });

        Assert.Equal(statut, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType!.MediaType);
        var probleme = (await reponse.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(((int)statut, code), (probleme["status"]!.GetValue<int>(), probleme["code"]!.GetValue<string>()));
    }

    [Fact]
    public async Task L_ETag_evite_de_retransferer_un_compte_qui_n_a_pas_change()
    {
        var premiere = await _systeme.Http.GetAsync("/comptes/10001");
        var etag = premiere.Headers.ETag!.Tag;

        var inchange = await Lire(etag);
        Assert.Equal(HttpStatusCode.NotModified, inchange.StatusCode);
        Assert.Empty(await inchange.Content.ReadAsByteArrayAsync());

        await _systeme.Rest.Virer("10001", "20001", 1m, "Café");

        var modifie = await Lire(etag);
        Assert.Equal(HttpStatusCode.OK, modifie.StatusCode);
        Assert.NotEqual(etag, modifie.Headers.ETag!.Tag);

        async Task<HttpResponseMessage> Lire(string etagConnu)
        {
            var requete = new HttpRequestMessage(HttpMethod.Get, "/comptes/10001");
            requete.Headers.IfNoneMatch.ParseAdd(etagConnu);
            return await _systeme.Http.SendAsync(requete);
        }
    }
}
