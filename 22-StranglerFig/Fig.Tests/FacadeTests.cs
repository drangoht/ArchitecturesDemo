using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Fig.Facade;
using Fig.Nouveau;
using static Fig.Tests.Systeme;

namespace Fig.Tests;

/// <summary>La façade : qui répond à quoi, la bascule à chaud dans les deux sens, le mode ombre, le retrait de l'ancien.</summary>
public class FacadeTests
{
    private static string ServiPar(HttpResponseMessage reponse) => reponse.Headers.GetValues("X-Servi-Par").Single();

    [Fact]
    public async Task Une_route_que_personne_n_a_reprise_va_a_l_ancien()
    {
        await using var s = new Systeme();

        var reponse = await s.Client.GetAsync("/agences");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("ancien", ServiPar(reponse));
        Assert.Equal(1, (await s.Json(s.Admin, "/facade/progression"))["appelsNonDeclares"]!.GetValue<long>());
    }

    [Fact]
    public async Task Les_routes_internes_ne_sont_pas_exposees_aux_clients()
    {
        await using var s = new Systeme();

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync("/interne/journal")).StatusCode);
        var frais = await s.Client.PostAsJsonAsync("/interne/frais", new Dictionary<string, object>
        {
            ["NUMCPT"] = Alice, ["MNTCTS"] = 100_000, ["LIBOPE"] = "PIRATAGE"
        });

        Assert.Equal(HttpStatusCode.NotFound, frais.StatusCode);
        Assert.Equal(1_000m, await s.Solde(s.AncienDirect, Alice));
    }

    [Fact]
    public async Task Basculer_une_route_change_qui_repond_sans_redemarrer_ni_rien_changer_pour_le_client()
    {
        await using var s = new Systeme();
        await s.Synchroniser();

        await s.Basculer("consultation", Mode.Ancien);
        var parLAncien = await s.Client.GetAsync($"/comptes/{Alice}");
        await s.Basculer("consultation", Mode.Nouveau);
        var parLeNouveau = await s.Client.GetAsync($"/comptes/{Alice}");

        Assert.Equal("ancien", ServiPar(parLAncien));
        Assert.Equal("nouveau", ServiPar(parLeNouveau));
        Assert.Equal(await parLAncien.Content.ReadAsStringAsync(), await parLeNouveau.Content.ReadAsStringAsync());
        var etat = await s.Admin.GetFromJsonAsync<List<EtatRoute>>("/facade/routes", Options);
        Assert.Equal((1, 1), etat!.Where(r => r.Nom == "consultation").Select(r => (r.AppelsAncien, r.AppelsNouveau)).Single());
    }

    [Fact]
    public async Task En_mode_ombre_le_client_recoit_la_reponse_de_l_ancien_et_les_deux_sont_comparees()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        await s.Basculer("operations", Mode.Ombre);

        var reponse = await s.Client.GetAsync($"/comptes/{Alice}/operations");

        Assert.Equal("ancien", ServiPar(reponse));
        var bilan = await s.AttendreComparaisons(b => b["comparees"]!.GetValue<long>() == 1);
        Assert.Equal(1, bilan["identiques"]!.GetValue<long>());
        Assert.Equal(0, bilan["divergences"]!.GetValue<long>());
    }

    [Fact]
    public async Task Une_divergence_est_signalee_sans_que_le_client_la_voie()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        await s.Basculer("operations", Mode.Ombre);
        // Un écart dans le nouveau service : une opération qu'il est seul à connaître (un bogue de reprise, par exemple).
        s.DepotDuNouveau.AppliquerDepuisAncien([new OperationEnregistree(Alice,
            new Operation("X-1", s.Horloge.GetUtcNow(), "FANTOME", -1m, Origine.Ancien))], s.DepotDuNouveau.PositionAncien);

        var reponse = await s.Client.GetStringAsync($"/comptes/{Alice}/operations");

        Assert.Equal(await s.AncienDirect.GetStringAsync($"/comptes/{Alice}/operations"), reponse);   // le client ne voit rien
        var bilan = await s.AttendreComparaisons(b => b["divergences"]!.GetValue<long>() == 1);
        var divergence = bilan["dernieresDivergences"]![0]!;
        Assert.Equal("operations", divergence["route"]!.GetValue<string>());
        Assert.Contains("FANTOME", divergence["corpsNouveau"]!.GetValue<string>());
    }

    [Fact]
    public async Task En_mode_ombre_un_nouveau_service_lent_ou_en_panne_ne_gene_pas_le_client()
    {
        await using var s = new Systeme(budgetOmbreMs: 2_000);
        await s.Synchroniser();
        await s.Basculer("operations", Mode.Ombre);
        await s.Client.GetAsync($"/comptes/{Alice}/operations");   // premier appel : démarrages, compilations
        await s.AttendreComparaisons(b => b["comparees"]!.GetValue<long>() == 1);

        s.FacadeVersNouveau.Latence = TimeSpan.FromSeconds(5);      // au-delà du budget de l'ombre (2 s)
        var chrono = Stopwatch.StartNew();
        var lente = await s.Client.GetAsync($"/comptes/{Alice}/operations");
        chrono.Stop();
        s.FacadeVersNouveau.Latence = TimeSpan.Zero;
        s.FacadeVersNouveau.Coupee = true;
        var enPanne = await s.Client.GetAsync($"/comptes/{Alice}/operations");

        Assert.Equal(HttpStatusCode.OK, lente.StatusCode);
        Assert.True(chrono.Elapsed < TimeSpan.FromSeconds(1.5), $"Le client a attendu {chrono.ElapsedMilliseconds} ms");
        Assert.Equal(HttpStatusCode.OK, enPanne.StatusCode);
        var bilan = await s.AttendreComparaisons(b => b["abandonnees"]!.GetValue<long>() == 2);
        Assert.Equal(0, bilan["divergences"]!.GetValue<long>());
    }

    [Fact]
    public async Task Le_mode_ombre_est_refuse_pour_une_ecriture()
    {
        await using var s = new Systeme();

        var reponse = await s.Admin.PutAsJsonAsync("/facade/routes/virements", new { mode = "Ombre" });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("deux fois", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Le_pilotage_de_la_migration_exige_la_cle_d_administration()
    {
        await using var s = new Systeme();

        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/facade/routes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.PutAsJsonAsync("/facade/routes/virements", new { mode = "Nouveau" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.PutAsJsonAsync("/facade/routes/inconnue", new { mode = "Nouveau" })).StatusCode);
    }

    [Fact]
    public async Task Revenir_en_arriere_sur_une_route_ne_perd_aucun_virement()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        await s.Basculer("virements", Mode.Nouveau);
        var parLeNouveau = await s.Client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 100 });
        Assert.Equal("nouveau", ServiPar(parLeNouveau));
        await s.Synchroniser();

        // Un problème en production : on rend la route à l'ancien. Il doit connaître le virement fait par le nouveau.
        await s.Basculer("virements", Mode.Ancien);
        var refuse = await s.Client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 950 });

        Assert.Equal("ancien", ServiPar(refuse));
        Assert.Equal(HttpStatusCode.BadRequest, refuse.StatusCode);   // 1 000 - 100 = 900 : le virement de 950 est impossible
        Assert.Contains("SOLDE INSUFFISANT", await refuse.Content.ReadAsStringAsync());
        Assert.Equal(900m, await s.Solde(s.AncienDirect, Alice));
    }

    [Fact]
    public async Task Ecrire_chez_l_un_et_lire_chez_l_autre_montre_un_solde_en_retard_jusqu_a_la_synchronisation()
    {
        // LIMITE de la démo, montrée plutôt que masquée : deux routes qui touchent les mêmes données, servies par deux
        // systèmes différents. D'où le conseil de basculer ENSEMBLE les lectures et les écritures d'une même donnée.
        await using var s = new Systeme();
        await s.Synchroniser();
        await s.Basculer("consultation", Mode.Nouveau);
        await s.Basculer("virements", Mode.Ancien);

        var virement = await s.Client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 100 });
        var avant = await s.Solde(s.Client, Alice);
        await s.Synchroniser();
        var apres = await s.Solde(s.Client, Alice);

        Assert.Equal(900m, JsonNode.Parse(await virement.Content.ReadAsStringAsync())!["soldeSource"]!.GetValue<decimal>());
        Assert.Equal(1_000m, avant);   // le nouveau ne sait pas encore
        Assert.Equal(900m, apres);
    }

    [Fact]
    public async Task L_ancien_n_est_retirable_que_lorsque_plus_rien_ne_l_appelle()
    {
        await using var s = new Systeme();
        await s.Client.GetAsync($"/comptes/{Alice}/releve");   // le relevé est encore servi par l'ancien
        await s.Basculer("consultation", Mode.Nouveau);
        Assert.False((await s.Json(s.Admin, "/facade/progression"))["ancienRetirable"]!.GetValue<bool>());

        await s.ToutBasculer(Mode.Nouveau);
        var toutMigre = await s.Json(s.Admin, "/facade/progression");
        await s.Client.GetAsync("/agences");   // une fonction que personne n'a recensée
        var apresAgences = await s.Json(s.Admin, "/facade/progression");

        Assert.Equal(100, toutMigre["pourcentage"]!.GetValue<int>());
        Assert.True(toutMigre["ancienRetirable"]!.GetValue<bool>());
        Assert.False(apresAgences["ancienRetirable"]!.GetValue<bool>());
        Assert.Equal(1, apresAgences["appelsAncienDepuisDerniereBascule"]!.GetValue<long>());
    }

    [Fact]
    public async Task Une_fois_tout_migre_l_ancien_peut_etre_eteint()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        await s.ToutBasculer(Mode.Nouveau);
        s.FacadeVersAncien.Coupee = true;     // l'ancien est éteint
        s.NouveauVersAncien.Coupee = true;

        var virement = await s.Client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 100 });
        var compte = await s.Client.GetAsync($"/comptes/{Alice}");
        var operations = await s.Client.GetAsync($"/comptes/{Alice}/operations");
        var releve = await s.Client.GetAsync($"/comptes/{Alice}/releve");
        var agences = await s.Client.GetAsync("/agences");

        foreach (var reponse in new[] { virement, compte, operations, releve })
        {
            Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
            Assert.Equal("nouveau", ServiPar(reponse));
        }
        Assert.Equal(900m, JsonNode.Parse(await compte.Content.ReadAsStringAsync())!["solde"]!.GetValue<decimal>());
        Assert.Equal(HttpStatusCode.BadGateway, agences.StatusCode);   // la fonction oubliée : il fallait la migrer aussi
    }

    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
