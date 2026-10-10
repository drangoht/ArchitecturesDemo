using System.Net;
using System.Net.Http.Json;
using Vertical.Api.Data;
using Vertical.Api.Features.Comptes;
using Vertical.Api.Features.Virements;

namespace Vertical.Tests.Features.Virements;

public class EffectuerVirementTests
{
    [Fact]
    public async Task Un_virement_deplace_l_argent_et_trace_les_operations()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        var reponse = await client.PostAsJsonAsync("/virements",
            new EffectuerVirement.Requete(BanqueDbContext.Alice, BanqueDbContext.Bob, 250m));

        var resultat = await reponse.Content.ReadFromJsonAsync<EffectuerVirement.Reponse>();
        Assert.Equal(new EffectuerVirement.Reponse(750m, 750m), resultat);

        var bob = await client.GetFromJsonAsync<ConsulterCompte.Reponse>($"/comptes/{BanqueDbContext.Bob}");
        Assert.Equal("Virement de Alice", bob!.DernieresOperations.Single().Libelle);
    }

    [Fact]
    public async Task Un_virement_vers_le_meme_compte_est_refuse_par_la_validation()
    {
        await using var app = new BanqueApplication();

        var reponse = await app.CreateClient().PostAsJsonAsync("/virements",
            new EffectuerVirement.Requete(BanqueDbContext.Alice, BanqueDbContext.Alice, 10m));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }
}
