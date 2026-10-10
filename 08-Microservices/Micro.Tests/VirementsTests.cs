using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Micro.Virements;

namespace Micro.Tests;

/// <summary>Le service Virements est testé seul : le service Comptes est un faux serveur HTTP.</summary>
public class VirementsTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    /// <summary>Simule le service Comptes. On choisit la réponse des débits et des crédits (numéro d'appel en paramètre).</summary>
    private static FauxHttp FauxComptes(
        Func<int, HttpResponseMessage>? debits = null,
        Func<Appel, HttpResponseMessage>? credits = null)
    {
        var numeroDebit = 0;
        return new FauxHttp((appel, _) =>
        {
            if (appel.Methode == HttpMethod.Get)
            {
                var id = Guid.Parse(appel.Chemin.Split('/').Last());
                return FauxHttp.Json(HttpStatusCode.OK, new { id, titulaire = id == Alice ? "Alice" : "Bob", solde = 1000m });
            }
            if (appel.Chemin.EndsWith("/debits"))
                return debits?.Invoke(Interlocked.Increment(ref numeroDebit)) ?? new HttpResponseMessage(HttpStatusCode.OK);
            return credits?.Invoke(appel) ?? new HttpResponseMessage(HttpStatusCode.OK);
        });
    }

    private static async Task<(HttpStatusCode Code, string Statut)> Virer(ServiceEnTest<Marqueur> service)
    {
        var reponse = await service.CreateClient().PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 50m });
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        return (reponse.StatusCode, corps.GetProperty("statut").GetString()!);
    }

    [Fact]
    public async Task Un_virement_reussi_publie_virement_effectue()
    {
        await using var service = new ServiceEnTest<Marqueur>(new() { ["comptes"] = FauxComptes() });

        var (code, statut) = await Virer(service);

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal("Effectue", statut);
        await Attente.Jusqua(() => service.Courtier.Appels.Any(a => a.Chemin == "/sujets/virement-effectue"));
    }

    [Fact]
    public async Task Une_panne_passagere_est_absorbee_par_le_retry_sans_double_debit()
    {
        // Le 1er appel de débit échoue (503), le retry automatique réussit.
        var comptes = FauxComptes(debits: numero => new HttpResponseMessage(
            numero == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        await using var service = new ServiceEnTest<Marqueur>(new() { ["comptes"] = comptes });

        var (_, statut) = await Virer(service);

        Assert.Equal("Effectue", statut);
        // Deux appels de débit… avec le MÊME operationId : Comptes ne débitera qu'une fois.
        var debits = comptes.Appels.Where(a => a.Chemin.EndsWith("/debits")).ToList();
        Assert.Equal(2, debits.Count);
        Assert.Single(debits.Select(a => JsonDocument.Parse(a.Corps!).RootElement.GetProperty("operationId").GetString()).Distinct());
    }

    [Fact]
    public async Task Si_le_credit_est_refuse_le_debit_est_compense()
    {
        var comptes = FauxComptes(credits: appel => appel.Chemin.Contains(Bob.ToString())
            ? FauxHttp.Json(HttpStatusCode.BadRequest, new { erreur = "Compte bloqué." })
            : new HttpResponseMessage(HttpStatusCode.OK));
        await using var service = new ServiceEnTest<Marqueur>(new() { ["comptes"] = comptes });

        var (code, statut) = await Virer(service);

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Equal("Annule", statut);
        var compensation = Assert.Single(comptes.Appels, a => a.Chemin == $"/interne/comptes/{Alice}/credits");
        Assert.Contains("-compensation", compensation.Corps);
        Assert.DoesNotContain(service.Courtier.Appels, a => a.Chemin == "/sujets/virement-effectue");
    }

    [Fact]
    public async Task Si_Comptes_est_injoignable_le_virement_est_marque_a_reprendre()
    {
        var injoignable = new FauxHttp((_, _) => throw new HttpRequestException("Connexion refusée"));
        await using var service = new ServiceEnTest<Marqueur>(new() { ["comptes"] = injoignable });

        var (code, statut) = await Virer(service);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, code);
        Assert.Equal("AReprendre", statut);
        Assert.Equal(4, injoignable.Appels.Count);  // 1 appel + 3 retries, puis abandon
    }
}
