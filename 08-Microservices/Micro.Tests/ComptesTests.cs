using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Micro.Comptes;

namespace Micro.Tests;

public class ComptesTests
{
    private static readonly Guid Alice = ComptesDb.Alice;

    private static async Task<decimal> Solde(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/comptes/{id}")).GetProperty("solde").GetDecimal();

    [Fact]
    public async Task Un_debit_rejoue_avec_le_meme_operationId_n_est_applique_qu_une_fois()
    {
        await using var service = new ServiceEnTest<Marqueur>();
        var client = service.CreateClient();
        var debit = new { operationId = "virement-42-debit", montant = 100m };

        var premier = await client.PostAsJsonAsync($"/interne/comptes/{Alice}/debits", debit);
        var rejoue = await client.PostAsJsonAsync($"/interne/comptes/{Alice}/debits", debit);  // retry réseau

        Assert.Equal(HttpStatusCode.OK, premier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejoue.StatusCode);
        Assert.Equal(900m, await Solde(client, Alice));
    }

    [Fact]
    public async Task Un_retrait_publie_retrait_effectue_via_l_outbox()
    {
        await using var service = new ServiceEnTest<Marqueur>();

        var reponse = await service.CreateClient().PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 100m });

        reponse.EnsureSuccessStatusCode();
        await Attente.Jusqua(() => service.Courtier.Appels.Any(a => a.Chemin == "/sujets/retrait-effectue"));
        var message = service.Courtier.Appels.Single(a => a.Chemin == "/sujets/retrait-effectue");
        Assert.Contains("\"nouveauSolde\":900", message.Corps);
        Assert.NotNull(message.MessageId);
    }

    [Fact]
    public async Task Si_le_courtier_est_en_panne_l_evenement_part_quand_il_revient()
    {
        // Le courtier échoue 3 fois, puis répond.
        var courtier = new FauxHttp((_, numero) => new HttpResponseMessage(
            numero <= 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted));
        await using var service = new ServiceEnTest<Marqueur>(new() { ["courtier"] = courtier });

        var reponse = await service.CreateClient().PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 100m });

        // Le retrait réussit tout de suite, même si le courtier est à terre…
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        // …et l'événement, resté dans l'outbox, finit par partir.
        await Attente.Jusqua(() => courtier.Appels.Count >= 4);
        Assert.Single(courtier.Appels.Select(a => a.MessageId).Distinct());
    }
}
