using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Micro.Tests;

public record Appel(HttpMethod Methode, string Hote, string Chemin, string? Corps, string? MessageId);

/// <summary>Faux serveur HTTP : enregistre les appels reçus et répond selon une fonction.</summary>
public class FauxHttp(Func<Appel, int, HttpResponseMessage> repondre) : HttpMessageHandler
{
    private int _compteur;

    public ConcurrentQueue<Appel> Appels { get; } = new();

    public static FauxHttp ToujoursOk() => new((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

    public static HttpResponseMessage Json(HttpStatusCode code, object corps) =>
        new(code) { Content = JsonContent.Create(corps) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken arret)
    {
        var corps = requete.Content is null ? null : await requete.Content.ReadAsStringAsync(arret);
        var appel = new Appel(requete.Method, requete.RequestUri!.Authority, requete.RequestUri.AbsolutePath, corps,
            requete.Headers.TryGetValues("Message-Id", out var ids) ? ids.First() : null);
        Appels.Enqueue(appel);
        return repondre(appel, Interlocked.Increment(ref _compteur));
    }
}

/// <summary>
/// Démarre UN service en mémoire, avec sa propre base. Ses dépendances réseau (autres services, courtier)
/// sont remplacées par de faux serveurs HTTP : c'est ainsi qu'on teste un microservice isolément.
/// </summary>
public class ServiceEnTest<TMarqueur>(
    Dictionary<string, FauxHttp>? fauxClients = null,
    Dictionary<string, string>? reglages = null) : WebApplicationFactory<TMarqueur>
    where TMarqueur : class
{
    private readonly Dictionary<string, FauxHttp> _faux = fauxClients ?? [];

    /// <summary>Le courtier est toujours simulé : un test ne doit jamais publier sur un vrai courtier.</summary>
    public FauxHttp Courtier => _faux.TryGetValue("courtier", out var courtier) ? courtier : _courtierParDefaut;
    private readonly FauxHttp _courtierParDefaut = FauxHttp.ToujoursOk();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("NomBase", Guid.NewGuid().ToString());
        foreach (var (cle, valeur) in reglages ?? [])
            builder.UseSetting(cle, valeur);

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("courtier").ConfigurePrimaryHttpMessageHandler(() => Courtier);
            foreach (var (nom, faux) in _faux.Where(f => f.Key != "courtier"))
                services.AddHttpClient(nom).ConfigurePrimaryHttpMessageHandler(() => faux);
        });
    }
}

public static class Attente
{
    /// <summary>Les traitements asynchrones (outbox, livraisons) prennent un peu de temps : on attend qu'ils aient lieu.</summary>
    public static async Task Jusqua(Func<bool> condition, int delaiMaxMs = 10_000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(delaiMaxMs);
        while (!condition() && DateTime.UtcNow < limite)
            await Task.Delay(50);
        Assert.True(condition(), "La condition attendue ne s'est pas produite à temps.");
    }
}
