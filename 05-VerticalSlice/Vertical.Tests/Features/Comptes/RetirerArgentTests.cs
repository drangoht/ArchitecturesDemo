using System.Net;
using System.Net.Http.Json;
using Vertical.Api.Data;
using Vertical.Api.Features.Comptes;

namespace Vertical.Tests.Features.Comptes;

public class RetirerArgentTests
{
    [Fact]
    public async Task Un_retrait_valide_debite_le_compte()
    {
        await using var app = new BanqueApplication();

        var reponse = await app.CreateClient()
            .PostAsJsonAsync($"/comptes/{BanqueDbContext.Alice}/retrait", new RetirerArgent.Requete(100m));

        reponse.EnsureSuccessStatusCode();
        var resultat = await reponse.Content.ReadFromJsonAsync<RetirerArgent.Reponse>();
        Assert.Equal(900m, resultat!.NouveauSolde);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]       // validation de la tranche
    [InlineData(99_999, HttpStatusCode.BadRequest)]  // règle du domaine (solde insuffisant)
    public async Task Un_retrait_invalide_est_refuse(int montant, HttpStatusCode attendu)
    {
        await using var app = new BanqueApplication();

        var reponse = await app.CreateClient()
            .PostAsJsonAsync($"/comptes/{BanqueDbContext.Alice}/retrait", new RetirerArgent.Requete(montant));

        Assert.Equal(attendu, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_compte_inconnu_renvoie_404()
    {
        await using var app = new BanqueApplication();

        var reponse = await app.CreateClient()
            .PostAsJsonAsync($"/comptes/{Guid.NewGuid()}/retrait", new RetirerArgent.Requete(10m));

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }
}
