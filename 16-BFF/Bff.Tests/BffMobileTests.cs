using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bff.Mobile;
using Bff.Services;

namespace Bff.Tests;

public class BffMobileTests
{
    [Fact]
    public async Task Sans_jeton_l_application_recoit_401()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Mobile.CreateClient().GetAsync("/mobile/v1/accueil");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task L_accueil_mobile_tient_en_une_requete_et_une_reponse_compacte()
    {
        await using var systeme = new Systeme();
        var telephone = await systeme.TelephoneConnecte();
        var navigateur = await systeme.NavigateurConnecte();

        var reponse = await telephone.GetAsync("/mobile/v1/accueil");
        var json = await reponse.Content.ReadAsStringAsync();
        var accueil = JsonDocument.Parse(json).RootElement;
        var web = await (await navigateur.GetAsync("/api/tableau-de-bord")).Content.ReadAsStringAsync();

        Assert.Equal(5, Systeme.EnTete(reponse, "X-Appels-Aval"));   // profil, 2 comptes, 2 × 3 opérations
        Assert.Equal(9_000m, accueil.GetProperty("soldeTotal").GetDecimal());
        Assert.Equal(3, accueil.GetProperty("dernieresOperations").GetArrayLength());
        Assert.DoesNotContain("iban", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CB ", json);
        Assert.True(json.Length * 5 < web.Length, $"Mobile : {json.Length} octets, web : {web.Length} octets.");
    }

    [Fact]
    public async Task La_v2_ajoute_un_bandeau_d_offre_sans_changer_la_v1()
    {
        await using var systeme = new Systeme();
        var telephone = await systeme.TelephoneConnecte();

        var v1 = await telephone.GetFromJsonAsync<JsonElement>("/mobile/v1/accueil");
        var v2 = await telephone.GetFromJsonAsync<JsonElement>("/mobile/v2/accueil");

        Assert.False(v1.TryGetProperty("offre", out _));
        Assert.Equal("Prêt immobilier à taux fixe", v2.GetProperty("offre").GetProperty("titre").GetString());
    }

    [Fact]
    public async Task Le_curseur_parcourt_toutes_les_operations_sans_doublon()
    {
        await using var systeme = new Systeme();
        var telephone = await systeme.TelephoneConnecte();
        var lues = new List<JsonElement>();
        var pages = 0;
        string? curseur = null;

        do
        {
            var url = $"/mobile/v1/comptes/{Banque.AliceCourant}/operations" + (curseur is null ? "" : $"?curseur={Uri.EscapeDataString(curseur)}");
            var page = await telephone.GetFromJsonAsync<JsonElement>(url);
            lues.AddRange(page.GetProperty("operations").EnumerateArray());
            curseur = page.TryGetProperty("suivant", out var suivant) ? suivant.GetString() : null;
            pages++;
        } while (curseur is not null);

        Assert.Equal(3, pages);
        Assert.Equal(25, lues.Count);
        Assert.Equal(25, lues.Select(o => o.GetRawText()).Distinct().Count());
    }

    [Theory]
    [InlineData("CB CARREFOUR 0910 PARIS 15", "Carrefour")]
    [InlineData("CB AMAZON EU SARL 0910", "Amazon EU")]
    [InlineData("PRLV SEPA EDF", "EDF")]
    [InlineData("PRLV SEPA FREE MOBILE", "Free Mobile")]
    [InlineData("RETRAIT DAB 0810 LYON", "Retrait d'espèces")]
    [InlineData("VIR SEPA SALAIRE ACME SAS", "Salaire")]
    [InlineData("LIBELLE INCONNU", "LIBELLE INCONNU")]
    public void Le_libelle_bancaire_brut_est_raccourci_pour_le_telephone(string brut, string attendu) =>
        Assert.Equal(attendu, Ecrans.LibelleCourt(brut));
}
