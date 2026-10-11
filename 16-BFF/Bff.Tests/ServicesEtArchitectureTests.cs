using System.Net;
using System.Net.Http.Json;
using Bff.Services;

namespace Bff.Tests;

public class ServicesEtArchitectureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Bearer n.importe.quoi")]
    [InlineData("Bearer eyJTdWIiOiJhbGljZSJ9.c2lnbmF0dXJl")]  // contenu « alice » avec une fausse signature
    public async Task Sans_jeton_valide_les_services_repondent_401(string? autorisation)
    {
        await using var systeme = new Systeme();
        var client = systeme.Services.CreateClient();
        if (autorisation is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", autorisation);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/profils/moi")).StatusCode);
    }

    [Fact]
    public async Task Un_mauvais_mot_de_passe_ne_donne_pas_de_jeton()
    {
        await using var systeme = new Systeme();

        var reponse = await systeme.Services.CreateClient().PostAsJsonAsync("/identite/jeton", new { utilisateur = "alice", motDePasse = "faux" });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_jeton_ne_donne_acces_qu_aux_comptes_de_son_titulaire()
    {
        await using var systeme = new Systeme();
        var client = systeme.Services.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await systeme.JetonServices("bob"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/comptes/{Banque.BobCourant}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/comptes/{Banque.AliceCourant}")).StatusCode);
    }

    [Fact]
    public void Chaque_BFF_ne_connait_les_services_et_l_autre_BFF_que_par_HTTP()
    {
        // Un BFF par application, chacun à son équipe : aucune référence de code entre eux, ni vers les services.
        string[] interdits = ["Bff.Services", "Bff.Web", "Bff.Mobile"];
        foreach (var bff in new[] { typeof(Bff.Web.Marqueur).Assembly, typeof(Bff.Mobile.Marqueur).Assembly })
        {
            var references = bff.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.Empty(references.Intersect(interdits));
        }
    }
}
