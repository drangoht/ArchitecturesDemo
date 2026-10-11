using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Styles.Banque;
using Styles.Client;
using Styles.Outillage.GraphQL;

namespace Styles.Tests;

/// <summary>Une horloge arrêtée qu'on règle à la main.</summary>
public sealed class HorlogeReglable(DateTimeOffset depart) : TimeProvider
{
    private DateTimeOffset _maintenant = depart;

    public override DateTimeOffset GetUtcNow() => _maintenant;

    public void Avancer(TimeSpan duree) => _maintenant += duree;
}

/// <summary>
/// Le serveur en mémoire, et les trois clients de l'application, chacun avec son compteur de requêtes et d'octets.
/// Les trois passent par le même serveur de test (gRPC compris : il gère HTTP/2 et les trailers).
/// </summary>
public sealed class Systeme : IAsyncDisposable
{
    // Jeudi 1er octobre 2026, 10 h 30 min 15,123 s à Paris.
    public HorlogeReglable Horloge { get; } = new(new DateTimeOffset(2026, 10, 1, 8, 30, 15, 123, TimeSpan.Zero));

    public WebApplicationFactory<Styles.Serveur.Marqueur> Serveur { get; }

    public CompteurHttp CompteurRest { get; }
    public CompteurHttp CompteurGraphQL { get; }
    public CompteurHttp CompteurGrpc { get; }

    public ClientRest Rest { get; }
    public ClientGraphQL GraphQL { get; }
    public ClientGrpcBanque Grpc { get; }

    /// <summary>Un client HTTP brut (sans compteur), pour regarder les réponses de près.</summary>
    public HttpClient Http { get; }

    public Systeme()
    {
        Serveur = new WebApplicationFactory<Styles.Serveur.Marqueur>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Horloge)));
        Http = Serveur.CreateClient();
        (CompteurRest, Rest) = Creer(h => new ClientRest(h));
        (CompteurGraphQL, GraphQL) = Creer(h => new ClientGraphQL(h));
        (CompteurGrpc, Grpc) = Creer(h => new ClientGrpcBanque(h));
    }

    private (CompteurHttp, T) Creer<T>(Func<HttpClient, T> fabrique)
    {
        var compteur = new CompteurHttp(Serveur.Server.CreateHandler());
        return (compteur, fabrique(new HttpClient(compteur) { BaseAddress = Serveur.Server.BaseAddress }));
    }

    public IEnumerable<IClientBanque> LesTrois => [Rest, GraphQL, Grpc];

    public ServiceBancaire Banque => Serveur.Services.GetRequiredService<ServiceBancaire>();

    public MoteurGraphQL Moteur => Serveur.Services.GetRequiredService<MoteurGraphQL>();

    /// <summary>Exécute une requête GraphQL directement sur le moteur (sans HTTP).</summary>
    public JsonObject Requete(string requete, object? variables = null) =>
        Moteur.Executer(requete, variables is null ? null : System.Text.Json.JsonSerializer.SerializeToNode(variables)!.AsObject());

    public async ValueTask DisposeAsync() => await Serveur.DisposeAsync();
}
