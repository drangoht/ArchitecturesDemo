using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Micro.Courtier;

namespace Micro.Tests;

public class CourtierTests
{
    [Fact]
    public async Task Un_message_est_relivre_jusqu_a_ce_que_l_abonne_reponde()
    {
        // L'abonné est en panne pour les 2 premières livraisons.
        var abonne = new FauxHttp((_, numero) => new HttpResponseMessage(
            numero <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        await using var service = new ServiceEnTest<Marqueur>(
            new() { ["abonnes"] = abonne },
            new() { ["Abonnements:test:0"] = "http://abonne.local/recevoir" });
        var client = service.CreateClient();

        var publication = await client.PostAsync("/sujets/test", JsonContent.Create(new { bonjour = "monde" }));

        Assert.Equal(HttpStatusCode.Accepted, publication.StatusCode);
        await Attente.Jusqua(() => abonne.Appels.Count >= 3);
        var etat = await client.GetFromJsonAsync<JsonElement>("/etat");
        Assert.Equal(1, etat.GetProperty("livrees").GetInt32());
        Assert.Equal(0, etat.GetProperty("enAttente").GetArrayLength());
        Assert.All(abonne.Appels, a => Assert.Equal("{\"bonjour\":\"monde\"}", a.Corps));
    }
}
