using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bff.Tests;

/// <summary>
/// Les trois processus de la démo, en mémoire : les services, et les deux BFF branchés dessus
/// (leur client HTTP passe par le serveur de test des services au lieu du réseau).
/// </summary>
public sealed class Systeme : IAsyncDisposable
{
    public WebApplicationFactory<Bff.Services.Marqueur> Services { get; }
    public WebApplicationFactory<Bff.Web.Marqueur> Web { get; }
    public WebApplicationFactory<Bff.Mobile.Marqueur> Mobile { get; }

    public Systeme(int latenceMs = 0, int budgetOffresMs = 2_000)
    {
        Services = new WebApplicationFactory<Bff.Services.Marqueur>()
            .WithWebHostBuilder(b => b.UseSetting("Services:LatenceMs", latenceMs.ToString()));
        Web = new WebApplicationFactory<Bff.Web.Marqueur>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Bff:BudgetOffresMs", budgetOffresMs.ToString());
            b.ConfigureTestServices(s => s.AddHttpClient<Bff.Web.ClientServices>()
                .ConfigurePrimaryHttpMessageHandler(() => Services.Server.CreateHandler()));
        });
        Mobile = new WebApplicationFactory<Bff.Mobile.Marqueur>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Bff:BudgetOffreMs", budgetOffresMs.ToString());
            b.ConfigureTestServices(s => s.AddHttpClient<Bff.Mobile.ClientServices>()
                .ConfigurePrimaryHttpMessageHandler(() => Services.Server.CreateHandler()));
        });
    }

    /// <summary>Un navigateur : il garde les cookies et ajoute l'en-tête anti-CSRF, comme le fait wwwroot/index.html.</summary>
    public HttpClient Navigateur(bool antiCsrf = true)
    {
        var client = Web.CreateClient();
        if (antiCsrf)
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }

    public async Task<HttpClient> NavigateurConnecte(string utilisateur = "alice")
    {
        var navigateur = Navigateur();
        var reponse = await navigateur.PostAsJsonAsync("/bff/connexion", new { utilisateur, motDePasse = "demo" });
        reponse.EnsureSuccessStatusCode();
        return navigateur;
    }

    /// <summary>L'application mobile : elle obtient son jeton et l'envoie elle-même à chaque requête.</summary>
    public async Task<HttpClient> TelephoneConnecte(string utilisateur = "alice")
    {
        var telephone = Mobile.CreateClient();
        var jeton = await (await telephone.PostAsJsonAsync("/mobile/v1/connexion", new { utilisateur, motDePasse = "demo" }))
            .Content.ReadFromJsonAsync<Bff.Mobile.JetonAval>();
        telephone.DefaultRequestHeaders.Authorization = new("Bearer", jeton!.Jeton);
        return telephone;
    }

    public async Task<string> JetonServices(string utilisateur)
    {
        var reponse = await Services.CreateClient().PostAsJsonAsync("/identite/jeton", new { utilisateur, motDePasse = "demo" });
        return (await reponse.Content.ReadFromJsonAsync<Bff.Web.JetonAval>())!.Jeton;
    }

    public static int EnTete(HttpResponseMessage reponse, string nom) => int.Parse(reponse.Headers.GetValues(nom).Single());

    public async ValueTask DisposeAsync()
    {
        await Web.DisposeAsync();
        await Mobile.DisposeAsync();
        await Services.DisposeAsync();
    }
}
