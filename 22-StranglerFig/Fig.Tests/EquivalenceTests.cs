using System.Net.Http.Json;
using static Fig.Tests.Systeme;

namespace Fig.Tests;

/// <summary>
/// Les tests de CONTRAT : pour chaque route que le nouveau service reprend, il doit répondre exactement comme l'ancien,
/// au caractère près (statut, type de contenu, corps), y compris pour les erreurs. C'est la condition pour basculer une
/// route sans que les clients le remarquent.
/// </summary>
public class EquivalenceTests
{
    /// <summary>Un historique varié, écrit des deux côtés, puis synchronisé.</summary>
    private static async Task<Systeme> SystemeAvecHistorique()
    {
        var s = new Systeme();
        await s.Synchroniser();
        s.Horloge.Avancer(TimeSpan.FromMinutes(5));
        await s.PreleverFrais(Alice, 250, "Frais de tenue de compte mensuels (offre essentielle)");   // libellé tronqué à 30
        s.Horloge.Avancer(TimeSpan.FromMinutes(5));
        (await s.AncienDirect.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 120.5m })).EnsureSuccessStatusCode();
        await s.Synchroniser();
        s.Horloge.Avancer(TimeSpan.FromMinutes(5));
        // Écrite dans l'ancien AVANT le virement suivant, mais connue du nouveau APRÈS lui : l'ordre d'arrivée n'est pas
        // celui des dates, et le contrat trie par date.
        await s.PreleverFrais(Alice, 99, "Commission d'intervention");
        s.Horloge.Avancer(TimeSpan.FromMinutes(5));
        // « 70.10 » : le nouveau service doit répondre 947.1 comme l'ancien, et non 947.10.
        (await s.NouveauDirect.PostAsJsonAsync("/virements", new { source = Bob, cible = Alice, montant = 70.10m })).EnsureSuccessStatusCode();
        // Le 31 octobre à 23 h 30 UTC, il est déjà 0 h 30 le 1er novembre à Paris (heure d'hiver) : relevé de novembre.
        s.Horloge.Regler(new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero));
        await s.PreleverFrais(Bob, 1_000, "Cotisation carte");
        await s.Synchroniser();
        await s.Synchroniser();
        return s;
    }

    public static TheoryData<string> Lectures => new()
    {
        $"/comptes/{Alice}",
        $"/comptes/{Bob}",
        $"/comptes/{Alice}/operations",
        $"/comptes/{Bob}/operations",
        $"/comptes/{Alice}/releve?mois=2026-10",
        $"/comptes/{Bob}/releve?mois=2026-10",
        $"/comptes/{Bob}/releve?mois=2026-11",
        $"/comptes/{Bob}/releve",
        $"/comptes/{Alice}/releve?mois=2026-09",
        $"/comptes/{Alice}/releve?mois=octobre",
        "/comptes/99999",
        "/comptes/99999/operations",
        "/comptes/99999/releve"
    };

    [Theory]
    [MemberData(nameof(Lectures))]
    public async Task Une_lecture_donne_la_meme_reponse_des_deux_cotes(string chemin)
    {
        await using var s = await SystemeAvecHistorique();

        var ancien = await s.AncienDirect.GetAsync(chemin);
        var nouveau = await s.NouveauDirect.GetAsync(chemin);

        Assert.Equal(ancien.StatusCode, nouveau.StatusCode);
        Assert.Equal(ancien.Content.Headers.ContentType, nouveau.Content.Headers.ContentType);
        Assert.Equal(await ancien.Content.ReadAsStringAsync(), await nouveau.Content.ReadAsStringAsync());
    }

    public static TheoryData<string, string, decimal> VirementsRefuses => new()
    {
        { Alice, Bob, 5_000m },     // SOLDE INSUFFISANT
        { Alice, Bob, 0m },         // MONTANT INVALIDE
        { Alice, Bob, -10m },
        { Alice, Bob, 1.005m },
        { Alice, Alice, 10m },      // COMPTES IDENTIQUES
        { Alice, "99999", 10m },    // COMPTE INCONNU
        { "99999", Bob, 10m }
    };

    [Theory]
    [MemberData(nameof(VirementsRefuses))]
    public async Task Un_virement_refuse_l_est_avec_la_meme_erreur_des_deux_cotes(string source, string cible, decimal montant)
    {
        await using var s = await SystemeAvecHistorique();
        var demande = new { source, cible, montant };

        var ancien = await s.AncienDirect.PostAsJsonAsync("/virements", demande);
        var nouveau = await s.NouveauDirect.PostAsJsonAsync("/virements", demande);

        Assert.False(ancien.IsSuccessStatusCode);
        Assert.Equal(ancien.StatusCode, nouveau.StatusCode);
        Assert.Equal(await ancien.Content.ReadAsStringAsync(), await nouveau.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Le_contrat_herite_garde_les_habitudes_de_l_ancien()
    {
        await using var s = await SystemeAvecHistorique();

        var operations = (await s.Json(s.NouveauDirect, $"/comptes/{Alice}/operations")).AsArray();
        var frais = operations.Single(o => o!["libelle"]!.GetValue<string>().StartsWith("FRAIS"))!;

        Assert.Equal("FRAIS DE TENUE DE COMPTE MENSU", frais["libelle"]!.GetValue<string>());   // majuscules, 30 caractères
        Assert.Equal("2026-10-12T10:05:00", frais["date"]!.GetValue<string>());                   // heure de Paris, sans fuseau
        Assert.Equal("-2.5", frais["montant"]!.ToJsonString());                                    // et non -2.50
    }

    [Fact]
    public async Task Un_virement_accepte_donne_le_meme_solde_mais_pas_la_meme_reference()
    {
        await using var s = await SystemeAvecHistorique();
        var demande = new { source = Alice, cible = Bob, montant = 100 };

        var ancien = await s.Json(s.AncienDirect, "/comptes/" + Alice);   // même point de départ des deux côtés
        Assert.Equal(ancien.ToJsonString(), (await s.Json(s.NouveauDirect, "/comptes/" + Alice)).ToJsonString());
        var reponseAncien = await (await s.AncienDirect.PostAsJsonAsync("/virements", demande)).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var reponseNouveau = await (await s.NouveauDirect.PostAsJsonAsync("/virements", demande)).Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(reponseAncien!["soldeSource"].ToString(), reponseNouveau!["soldeSource"].ToString());
        // La référence est opaque pour les clients : chaque système a sa numérotation, sans collision possible.
        Assert.StartsWith("VIR-0", reponseAncien["reference"].ToString());
        Assert.StartsWith("VIR-N", reponseNouveau["reference"].ToString());
    }
}
