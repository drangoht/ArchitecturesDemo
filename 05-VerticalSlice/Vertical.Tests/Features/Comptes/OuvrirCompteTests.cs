using System.Net;
using System.Net.Http.Json;
using Vertical.Api.Features.Comptes;

namespace Vertical.Tests.Features.Comptes;

// Les tests suivent la même organisation que le code : un fichier de test par tranche.
public class OuvrirCompteTests
{
    [Fact]
    public async Task Ouvrir_un_compte_renvoie_201_et_le_compte_est_consultable()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        var reponse = await client.PostAsJsonAsync("/comptes", new OuvrirCompte.Requete("Chloé", 50m));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        var compte = await reponse.Content.ReadFromJsonAsync<OuvrirCompte.Reponse>();
        var consultation = await client.GetFromJsonAsync<ConsulterCompte.Reponse>($"/comptes/{compte!.Id}");
        Assert.Equal(50m, consultation!.Solde);
        Assert.Equal("Dépôt initial", consultation.DernieresOperations.Single().Libelle);
    }

    [Fact]
    public async Task Une_requete_invalide_renvoie_les_erreurs_de_validation()
    {
        await using var app = new BanqueApplication();

        var reponse = await app.CreateClient().PostAsJsonAsync("/comptes", new OuvrirCompte.Requete("", 1m));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("Titulaire", corps);
        Assert.Contains("DepotInitial", corps);
    }
}
