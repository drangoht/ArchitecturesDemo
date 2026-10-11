using System.Net;
using System.Net.Http.Json;
using static Fig.Tests.Systeme;

namespace Fig.Tests;

/// <summary>Tant que les deux systèmes vivent, ils doivent dire la même chose : dans les deux sens.</summary>
public class SynchronisationTests
{
    [Fact]
    public async Task Le_premier_cycle_recharge_tout_l_ancien()
    {
        await using var s = new Systeme();
        Assert.Equal(HttpStatusCode.NotFound, (await s.NouveauDirect.GetAsync($"/comptes/{Alice}")).StatusCode);

        var bilan = await s.Synchroniser();

        Assert.Equal(4, bilan.LignesLues);   // 2 ouvertures de compte, 2 versements initiaux
        foreach (var numero in new[] { Alice, Bob })
            Assert.Equal(await s.AncienDirect.GetStringAsync($"/comptes/{numero}"), await s.NouveauDirect.GetStringAsync($"/comptes/{numero}"));
    }

    [Fact]
    public async Task Ce_qu_ecrit_un_traitement_interne_de_l_ancien_arrive_dans_le_nouveau()
    {
        await using var s = new Systeme();
        await s.Synchroniser();

        await s.PreleverFrais(Alice, 250, "Frais de tenue de compte");
        Assert.Equal(1_000m, await s.Solde(s.NouveauDirect, Alice));   // pas encore
        var bilan = await s.Synchroniser();

        Assert.Equal(1, bilan.FaitsAppliques);
        Assert.Equal(997.5m, await s.Solde(s.NouveauDirect, Alice));
        Assert.Equal(await s.AncienDirect.GetStringAsync($"/comptes/{Alice}/operations"),
            await s.NouveauDirect.GetStringAsync($"/comptes/{Alice}/operations"));
    }

    [Fact]
    public async Task Un_virement_fait_dans_le_nouveau_est_reporte_dans_l_ancien()
    {
        await using var s = new Systeme();
        await s.Synchroniser();

        (await s.NouveauDirect.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 50 })).EnsureSuccessStatusCode();
        Assert.Equal(1_000m, await s.Solde(s.AncienDirect, Alice));   // pas encore
        var bilan = await s.Synchroniser();

        Assert.Equal(2, bilan.Reportees);
        Assert.Equal(950m, await s.Solde(s.AncienDirect, Alice));
        Assert.Equal(550m, await s.Solde(s.AncienDirect, Bob));
        Assert.Equal(await s.NouveauDirect.GetStringAsync($"/comptes/{Alice}/releve"), await s.AncienDirect.GetStringAsync($"/comptes/{Alice}/releve"));
    }

    [Fact]
    public async Task L_echo_d_un_virement_du_nouveau_ne_revient_pas_en_double()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        (await s.NouveauDirect.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 50 })).EnsureSuccessStatusCode();

        await s.Synchroniser();   // reporte le virement dans l'ancien, qui l'inscrit dans son journal
        var echo = await s.Synchroniser();   // relit ces deux lignes : ce sont nos propres opérations
        await s.Synchroniser();

        Assert.Equal(2, echo.LignesLues);
        Assert.Equal(0, echo.FaitsAppliques);
        Assert.Equal(950m, await s.Solde(s.NouveauDirect, Alice));
        Assert.Equal(2, (await s.Json(s.NouveauDirect, $"/comptes/{Alice}/operations")).AsArray().Count);
        Assert.Equal(await s.AncienDirect.GetStringAsync($"/comptes/{Alice}/operations"),
            await s.NouveauDirect.GetStringAsync($"/comptes/{Alice}/operations"));
    }

    [Fact]
    public async Task Si_l_ancien_est_injoignable_les_operations_attendent_le_cycle_suivant()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        (await s.NouveauDirect.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 50 })).EnsureSuccessStatusCode();

        s.NouveauVersAncien.EcrituresCoupees = true;   // le journal se lit, l'import échoue
        await Assert.ThrowsAsync<HttpRequestException>(s.Synchroniser);
        Assert.Equal(2, s.DepotDuNouveau.AReporter().Count);
        Assert.Equal(1_000m, await s.Solde(s.AncienDirect, Alice));

        s.NouveauVersAncien.EcrituresCoupees = false;
        await s.Synchroniser();
        Assert.Empty(s.DepotDuNouveau.AReporter());
        Assert.Equal(950m, await s.Solde(s.AncienDirect, Alice));
    }

    [Fact]
    public async Task Un_lot_renvoye_apres_une_reponse_perdue_n_est_importe_qu_une_fois()
    {
        await using var s = new Systeme();
        await s.Synchroniser();
        (await s.NouveauDirect.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 50 })).EnsureSuccessStatusCode();

        s.NouveauVersAncien.ReponsesAuxEcrituresPerdues = true;   // l'ancien importe, mais le nouveau ne le sait pas
        await Assert.ThrowsAsync<HttpRequestException>(s.Synchroniser);
        Assert.Equal(950m, await s.Solde(s.AncienDirect, Alice));
        s.NouveauVersAncien.ReponsesAuxEcrituresPerdues = false;
        await s.Synchroniser();                          // le même lot repart

        Assert.Equal(950m, await s.Solde(s.AncienDirect, Alice));
        Assert.Equal(550m, await s.Solde(s.AncienDirect, Bob));
    }
}
