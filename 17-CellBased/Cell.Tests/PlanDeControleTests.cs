using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cell.Tests;

/// <summary>Le plan de contrôle : placement, ajout de cellules, migration, déploiement par vagues.</summary>
public class PlanDeControleTests
{
    [Fact]
    public async Task Un_nouveau_client_va_dans_la_cellule_la_moins_chargee()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Client().PostAsJsonAsync("/clients", new { id = "farid", nom = "Farid", depotInitial = 50 });

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.Equal("c3", (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cellule").GetString());
        Assert.Equal(50m, await systeme.Solde("farid"));
    }

    [Fact]
    public async Task Quand_les_cellules_sont_pleines_on_en_ajoute_une_au_lieu_de_les_agrandir()
    {
        await using var systeme = new Systeme();
        foreach (var id in new[] { "g1", "g2", "g3", "g4" })   // 5 + 4 = 9 = 3 cellules × 3 places
            (await systeme.Client().PostAsJsonAsync("/clients", new { id, nom = id })).EnsureSuccessStatusCode();

        var refus = await systeme.Client().PostAsJsonAsync("/clients", new { id = "g5", nom = "g5" });
        var ajout = await systeme.Client().PostAsJsonAsync("/plateforme/cellules", new { id = "c4", url = "http://localhost:5234", capacite = 3 });
        var accepte = await systeme.Client().PostAsJsonAsync("/clients", new { id = "g5", nom = "g5" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refus.StatusCode);
        Assert.Contains("pleines", await refus.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, ajout.StatusCode);
        Assert.Equal("c4", (await accepte.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cellule").GetString());
    }

    [Fact]
    public async Task Une_cellule_fermee_ne_recoit_plus_de_nouveaux_clients()
    {
        await using var systeme = new Systeme();
        await systeme.Client().PostAsync("/plateforme/cellules/c3/fermer", null);

        var reponse = await systeme.Client().PostAsJsonAsync("/clients", new { id = "farid", nom = "Farid" });

        Assert.NotEqual("c3", (await reponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cellule").GetString());
        Assert.Equal(HttpStatusCode.OK, (await systeme.Client().GetAsync("/clients/carla/comptes")).StatusCode);  // ses clients restent servis
    }

    [Fact]
    public async Task Migrer_un_client_deplace_ses_donnees_et_son_routage()
    {
        await using var systeme = new Systeme();
        await systeme.Client().PostAsJsonAsync($"/clients/bob/comptes/{Systeme.Bob}/retrait", new { montant = 50 });

        var bilan = await (await systeme.Client().PostAsJsonAsync("/plateforme/clients/bob/migrer", new { vers = "c3" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var apres = await systeme.Client().GetAsync("/clients/bob/comptes");

        Assert.Equal("c2", bilan.GetProperty("de").GetString());
        Assert.Equal("c3", apres.Headers.GetValues("X-Cellule").Single());
        Assert.Equal(450m, await systeme.Solde("bob"));
        Assert.Equal(1, (await systeme.Client().GetFromJsonAsync<JsonElement>($"/clients/bob/comptes/{Systeme.Bob}/mouvements")).GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await systeme.Direct("c2").GetAsync("/clients/bob/comptes")).StatusCode);
    }

    [Fact]
    public async Task Les_operations_deja_traitees_suivent_le_client_dans_sa_nouvelle_cellule()
    {
        await using var systeme = new Systeme();
        var virement = await (await systeme.Client().PostAsJsonAsync("/clients/alice/virements",
            new { source = Systeme.Alice, clientCible = "bob", cible = Systeme.Bob, montant = 100 })).Content.ReadFromJsonAsync<JsonElement>();
        await systeme.Client().PostAsJsonAsync("/plateforme/clients/bob/migrer", new { vers = "c3" });

        await systeme.Client().PostAsJsonAsync("/interne/clients/bob/credits",
            new { operationId = virement.GetProperty("operationId").GetGuid(), compte = Systeme.Bob, montant = 100, libelle = "rejeu" });

        Assert.Equal(600m, await systeme.Solde("bob"));
    }

    [Fact]
    public async Task La_sonde_de_sante_detecte_le_bogue_de_la_version_1_1()
    {
        await using var systeme = new Systeme();
        await systeme.Direct("c1").PutAsJsonAsync("/admin/version", new { version = "1.1" });

        var sante = await systeme.Direct("c1").GetFromJsonAsync<JsonElement>("/sante");
        await systeme.Client().PostAsJsonAsync($"/clients/alice/comptes/{Systeme.Alice}/retrait", new { montant = 10 });

        Assert.False(sante.GetProperty("sain").GetBoolean());
        Assert.Equal(980m, await systeme.Solde("alice"));   // le bogue : 10 € retirés, 20 € débités
        Assert.Equal(500m, await systeme.Solde("bob"));     // c2 n'a pas reçu cette version
    }

    [Fact]
    public async Task Un_deploiement_defectueux_s_arrete_a_la_cellule_canari()
    {
        await using var systeme = new Systeme();

        var bilan = await (await systeme.Client().PostAsJsonAsync("/plateforme/deploiements", new { version = "1.1" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Annule", bilan.GetProperty("statut").GetString());
        Assert.Equal(1, bilan.GetProperty("vagues").GetArrayLength());   // la 2e vague n'a jamais commencé
        Assert.Contains("retour arrière", bilan.GetProperty("vagues")[0].GetProperty("resultat").GetString());
        foreach (var cellule in new[] { "c1", "c2", "c3" })
            Assert.Equal("1.0", await systeme.Version(cellule));
        // Seule la cellule canari a fait tourner la version défectueuse ; ses clients sont les seuls à avoir pu la subir.
        Assert.Equal(["1.0", "1.1", "1.0"], await systeme.Historique("c1"));
        Assert.Equal(["1.0"], await systeme.Historique("c2"));
        Assert.Equal(["1.0"], await systeme.Historique("c3"));
    }

    [Fact]
    public async Task Un_deploiement_sain_passe_vague_par_vague()
    {
        await using var systeme = new Systeme();

        var bilan = await (await systeme.Client().PostAsJsonAsync("/plateforme/deploiements", new { version = "1.2" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Termine", bilan.GetProperty("statut").GetString());
        Assert.Equal(["c1"], bilan.GetProperty("vagues")[0].GetProperty("cellules").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(["c2", "c3"], bilan.GetProperty("vagues")[1].GetProperty("cellules").EnumerateArray().Select(c => c.GetString()));
        foreach (var cellule in new[] { "c1", "c2", "c3" })
            Assert.Equal("1.2", await systeme.Version(cellule));
    }

    [Fact]
    public async Task Une_cellule_malade_arrete_le_deploiement_a_sa_vague()
    {
        await using var systeme = new Systeme();
        await systeme.MettreEnPanne("c3");   // en panne mais joignable : sa sonde ne répond plus

        var bilan = await (await systeme.Client().PostAsJsonAsync("/plateforme/deploiements", new { version = "1.2" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Annule", bilan.GetProperty("statut").GetString());
        Assert.Equal("saine", bilan.GetProperty("vagues")[0].GetProperty("resultat").GetString());
        Assert.Equal("1.2", await systeme.Version("c1"));   // la vague 1, saine, est conservée
        Assert.Equal("1.0", await systeme.Version("c2"));   // la vague 2 est revenue en arrière
    }

    [Fact]
    public void Le_routeur_et_les_cellules_ne_partagent_aucun_code()
    {
        Assert.DoesNotContain(typeof(Cell.Routeur.Marqueur).Assembly.GetReferencedAssemblies(), a => a.Name == "Cell.Cellule");
        Assert.DoesNotContain(typeof(Cell.Cellule.Marqueur).Assembly.GetReferencedAssemblies(), a => a.Name == "Cell.Routeur");
    }
}
