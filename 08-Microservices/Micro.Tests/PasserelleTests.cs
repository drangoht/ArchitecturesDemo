using System.Net;
using System.Net.Http.Json;
using Micro.Passerelle;

namespace Micro.Tests;

public class PasserelleTests
{
    [Fact]
    public async Task La_passerelle_route_chaque_prefixe_vers_son_service()
    {
        var aval = FauxHttp.ToujoursOk();
        await using var service = new ServiceEnTest<Marqueur>(new() { ["aval"] = aval });
        var client = service.CreateClient();

        await client.GetAsync("/comptes/123");
        await client.PostAsJsonAsync("/virements", new { montant = 10 });

        Assert.Equal(
            [("localhost:5201", "/comptes/123"), ("localhost:5202", "/virements")],
            aval.Appels.Select(a => (a.Hote, a.Chemin)));
        Assert.Equal("{\"montant\":10}", aval.Appels.Last().Corps);
    }

    [Fact]
    public async Task L_API_interne_des_services_n_est_pas_exposee()
    {
        var aval = FauxHttp.ToujoursOk();
        await using var service = new ServiceEnTest<Marqueur>(new() { ["aval"] = aval });

        var reponse = await service.CreateClient().PostAsJsonAsync("/interne/comptes/123/debits", new { montant = 1000 });

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.Empty(aval.Appels);
    }

    [Fact]
    public async Task Un_service_injoignable_donne_502()
    {
        var injoignable = new FauxHttp((_, _) => throw new HttpRequestException("Connexion refusée"));
        await using var service = new ServiceEnTest<Marqueur>(new() { ["aval"] = injoignable });

        var reponse = await service.CreateClient().GetAsync("/notifications/123");

        Assert.Equal(HttpStatusCode.BadGateway, reponse.StatusCode);
    }
}
