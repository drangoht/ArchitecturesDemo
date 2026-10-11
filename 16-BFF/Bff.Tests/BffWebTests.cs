using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bff.Services;

namespace Bff.Tests;

public class BffWebTests
{
    [Fact]
    public async Task La_connexion_pose_un_cookie_HttpOnly_et_ne_renvoie_aucun_jeton()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Navigateur().PostAsJsonAsync("/bff/connexion", new { utilisateur = "alice", motDePasse = "demo" });

        var cookie = reponse.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bff-session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("jeton", corps, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("""{"utilisateur":"alice"}""", corps);
    }

    [Fact]
    public async Task Sans_session_l_API_repond_401_et_non_une_redirection()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Navigateur().GetAsync("/api/tableau-de-bord");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Sans_en_tete_anti_CSRF_une_requete_est_refusee_meme_avec_une_session()
    {
        await using var systeme = new Systeme();
        var navigateur = await systeme.NavigateurConnecte();
        navigateur.DefaultRequestHeaders.Remove("X-CSRF");  // ce que ferait un formulaire piégé sur un autre site

        var reponse = await navigateur.PostAsJsonAsync("/api/virements", new { source = Banque.AliceCourant, cible = Banque.BobCourant, montant = 100 });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("X-CSRF", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Le_tableau_de_bord_agrege_sept_appels_faits_en_parallele()
    {
        const int latence = 150;
        await using var systeme = new Systeme(latenceMs: latence);
        var navigateur = await systeme.NavigateurConnecte();

        var reponse = await navigateur.GetAsync("/api/tableau-de-bord");
        var tableau = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        // profil, puis en parallèle : conseiller, offres, 2 comptes, 2 listes d'opérations
        Assert.Equal(7, Systeme.EnTete(reponse, "X-Appels-Aval"));
        // En parallèle : 2 vagues (le profil, puis tout le reste), soit environ 2 latences ; enchaînés, il en faudrait 7.
        var duree = Systeme.EnTete(reponse, "X-Duree-Totale-Ms");
        Assert.True(duree < 4 * latence, $"{duree} ms : les appels auraient dû se chevaucher.");
        Assert.True(Systeme.EnTete(reponse, "X-Duree-Aval-Cumulee-Ms") >= 7 * latence);
        Assert.Equal(2, tableau.GetProperty("comptes").GetArrayLength());
        Assert.Equal(10, tableau.GetProperty("comptes")[0].GetProperty("operations").GetArrayLength());
        Assert.Equal("Claire Dubois", tableau.GetProperty("client").GetProperty("conseiller").GetProperty("nom").GetString());
        Assert.NotEqual(0, tableau.GetProperty("depensesParCategorie").GetArrayLength());
        Assert.Equal(2, tableau.GetProperty("offres").GetArrayLength());
        Assert.Equal(0, tableau.GetProperty("degrade").GetArrayLength());
    }

    [Fact]
    public async Task Une_panne_des_offres_degrade_le_tableau_de_bord_sans_le_faire_echouer()
    {
        await using var systeme = new Systeme();
        await systeme.Services.CreateClient().PutAsJsonAsync("/chaos", new { offresEnPanne = true });
        var navigateur = await systeme.NavigateurConnecte();

        var tableau = await navigateur.GetFromJsonAsync<JsonElement>("/api/tableau-de-bord");

        Assert.Equal(JsonValueKind.Null, tableau.GetProperty("offres").ValueKind);
        Assert.Equal("offres", tableau.GetProperty("degrade")[0].GetString());
        Assert.Equal(2, tableau.GetProperty("comptes").GetArrayLength());
    }

    [Fact]
    public async Task Un_service_trop_lent_est_abandonne_au_bout_de_son_budget()
    {
        await using var systeme = new Systeme(latenceMs: 300, budgetOffresMs: 100);
        var navigateur = await systeme.NavigateurConnecte();

        var tableau = await navigateur.GetFromJsonAsync<JsonElement>("/api/tableau-de-bord");

        Assert.Equal("offres", tableau.GetProperty("degrade")[0].GetString());
        Assert.Equal(2, tableau.GetProperty("comptes").GetArrayLength());
    }

    [Fact]
    public async Task Un_virement_passe_par_la_session_et_les_droits_de_l_utilisateur()
    {
        await using var systeme = new Systeme();
        var navigateur = await systeme.NavigateurConnecte();

        var virement = await navigateur.PostAsJsonAsync("/api/virements", new { source = Banque.AliceCourant, cible = Banque.BobCourant, montant = 25 });
        // Bob est connecté ailleurs ; Alice tente de débiter le compte de Bob : les services refusent, le BFF n'a aucun passe-droit.
        var debitDeBob = await navigateur.PostAsJsonAsync("/api/virements", new { source = Banque.BobCourant, cible = Banque.AliceCourant, montant = 25 });

        Assert.Equal(975m, (await virement.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("solde").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, debitDeBob.StatusCode);
    }

    [Fact]
    public async Task Apres_deconnexion_un_cookie_vole_ne_vaut_plus_rien()
    {
        await using var systeme = new Systeme();
        var connexion = await systeme.Navigateur().PostAsJsonAsync("/bff/connexion", new { utilisateur = "alice", motDePasse = "demo" });
        var cookie = connexion.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bff-session=")).Split(';')[0];
        var voleur = systeme.Web.CreateClient(new() { HandleCookies = false });
        voleur.DefaultRequestHeaders.Add("X-CSRF", "1");
        voleur.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await voleur.GetAsync("/bff/utilisateur")).StatusCode);

        var deconnexion = new HttpRequestMessage(HttpMethod.Post, "/bff/deconnexion");
        deconnexion.Headers.Add("Cookie", cookie);
        await voleur.SendAsync(deconnexion);

        // La session est supprimée côté serveur : le même cookie, rejoué, ne mène plus à rien.
        Assert.Equal(HttpStatusCode.Unauthorized, (await voleur.GetAsync("/bff/utilisateur")).StatusCode);
    }

    [Fact]
    public async Task Le_site_est_servi_par_son_BFF_sur_la_meme_origine()
    {
        await using var systeme = new Systeme();

        var page = await systeme.Navigateur(antiCsrf: false).GetStringAsync("/");

        Assert.Contains("X-CSRF", page);
        Assert.DoesNotContain("localStorage.setItem", page);
    }
}
