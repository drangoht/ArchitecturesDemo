using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Modulaire.Comptes;

namespace Modulaire.Tests;

// Parcours de bout en bout à travers plusieurs modules.
public class ParcoursTests
{
    private static readonly Guid Alice = ComptesModule.Alice;
    private static readonly Guid Bob = ComptesModule.Bob;

    private static async Task<string[]> Notifications(HttpClient client, Guid compteId)
    {
        var json = await client.GetFromJsonAsync<JsonElement>($"/notifications/{compteId}");
        return json.EnumerateArray().Select(n => n.GetProperty("message").GetString()!).ToArray();
    }

    [Fact]
    public async Task Un_virement_traverse_les_trois_modules()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        var reponse = await client.PostAsJsonAsync("/virements", new { source = Alice, cible = Bob, montant = 250m });

        reponse.EnsureSuccessStatusCode();
        // Module Comptes : les soldes ont bougé (appel synchrone depuis Virements).
        var bob = await client.GetFromJsonAsync<JsonElement>($"/comptes/{Bob}");
        Assert.Equal(750m, bob.GetProperty("solde").GetDecimal());
        // Module Virements : historique dans sa propre base.
        var historique = await client.GetFromJsonAsync<JsonElement>($"/virements/compte/{Bob}");
        Assert.Equal(1, historique.GetArrayLength());
        // Module Notifications : alimenté uniquement par l'événement VirementEffectue.
        Assert.Contains("reçu de Alice", Assert.Single(await Notifications(client, Bob)));
        Assert.Contains("envoyé à Bob", Assert.Single(await Notifications(client, Alice)));
    }

    [Fact]
    public async Task Un_retrait_publie_un_evenement_qui_cree_une_notification()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        await client.PostAsJsonAsync($"/comptes/{Alice}/retrait", new { montant = 100m });

        Assert.StartsWith("Retrait de 100", Assert.Single(await Notifications(client, Alice)));
    }

    [Fact]
    public async Task Un_virement_refuse_ne_publie_rien()
    {
        await using var app = new BanqueApplication();
        var client = app.CreateClient();

        var reponse = await client.PostAsJsonAsync("/virements", new { source = Bob, cible = Alice, montant = 99_999m });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Empty(await Notifications(client, Bob));
    }
}
