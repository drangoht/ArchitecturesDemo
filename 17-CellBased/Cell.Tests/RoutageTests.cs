using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cell.Tests;

/// <summary>Le plan de données : chaque client va vers SA cellule, et une panne ne touche qu'une cellule.</summary>
public class RoutageTests
{
    [Fact]
    public async Task Les_clients_de_demo_sont_repartis_entre_les_cellules()
    {
        await using var systeme = new Systeme();

        var placement = await systeme.Placement();

        Assert.Equal("c1", placement.GetProperty("alice").GetString());
        Assert.Equal("c2", placement.GetProperty("bob").GetString());
        Assert.Equal("c3", placement.GetProperty("carla").GetString());
        Assert.Equal("c1", placement.GetProperty("david").GetString());
        Assert.Equal("c2", placement.GetProperty("emma").GetString());
    }

    [Theory]
    [InlineData("alice", "c1")]
    [InlineData("bob", "c2")]
    [InlineData("carla", "c3")]
    public async Task Le_routeur_envoie_chaque_client_vers_sa_cellule(string client, string cellule)
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Client().GetAsync($"/clients/{client}/comptes");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(cellule, reponse.Headers.GetValues("X-Cellule").Single());
    }

    [Fact]
    public async Task Une_cellule_ne_contient_que_ses_clients()
    {
        await using var systeme = new Systeme();

        Assert.Equal(HttpStatusCode.OK, (await systeme.Direct("c1").GetAsync("/clients/alice/comptes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await systeme.Direct("c1").GetAsync("/clients/bob/comptes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await systeme.Client().GetAsync("/clients/inconnu/comptes")).StatusCode);
    }

    [Fact]
    public async Task Une_panne_de_cellule_ne_touche_que_ses_clients()
    {
        await using var systeme = new Systeme();
        await systeme.MettreEnPanne("c2");

        var statuts = new Dictionary<string, HttpStatusCode>();
        foreach (var client in new[] { "alice", "bob", "carla", "david", "emma" })
            statuts[client] = (await systeme.Client().GetAsync($"/clients/{client}/comptes")).StatusCode;

        // Rayon d'impact : 2 clients sur 5 (ceux de c2), et non tous.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, statuts["bob"]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, statuts["emma"]);
        Assert.All(new[] { "alice", "carla", "david" }, c => Assert.Equal(HttpStatusCode.OK, statuts[c]));
    }

    [Fact]
    public async Task Un_virement_dans_une_meme_cellule_reste_local()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Client().PostAsJsonAsync("/clients/alice/virements",
            new { source = Systeme.Alice, clientCible = "david", cible = Systeme.David, montant = 100 });

        Assert.False((await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("interCellules").GetBoolean());
        Assert.Equal(900m, await systeme.Solde("alice"));
        Assert.Equal(400m, await systeme.Solde("david"));
    }

    [Fact]
    public async Task Un_virement_entre_cellules_passe_par_le_routeur_et_ne_credite_qu_une_fois()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Client().PostAsJsonAsync("/clients/alice/virements",
            new { source = Systeme.Alice, clientCible = "bob", cible = Systeme.Bob, montant = 100 });
        var virement = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        // Le même crédit rejoué (nouvel essai après une réponse perdue) : sans effet.
        await systeme.Client().PostAsJsonAsync("/interne/clients/bob/credits",
            new { operationId = virement.GetProperty("operationId").GetGuid(), compte = Systeme.Bob, montant = 100, libelle = "Virement de alice" });

        Assert.True(virement.GetProperty("interCellules").GetBoolean());
        Assert.Equal(900m, await systeme.Solde("alice"));
        Assert.Equal(600m, await systeme.Solde("bob"));
    }

    [Fact]
    public async Task Si_la_cellule_du_beneficiaire_est_en_panne_le_virement_est_compense()
    {
        await using var systeme = new Systeme();
        await systeme.MettreEnPanne("c2");

        var reponse = await systeme.Client().PostAsJsonAsync("/clients/alice/virements",
            new { source = Systeme.Alice, clientCible = "bob", cible = Systeme.Bob, montant = 100 });

        Assert.Equal(HttpStatusCode.BadGateway, reponse.StatusCode);
        Assert.Equal(1_000m, await systeme.Solde("alice"));
        var mouvements = await systeme.Client().GetFromJsonAsync<JsonElement>($"/clients/alice/comptes/{Systeme.Alice}/mouvements");
        Assert.Contains(mouvements.EnumerateArray(), m => m.GetProperty("libelle").GetString()!.StartsWith("Annulation"));
    }
}
