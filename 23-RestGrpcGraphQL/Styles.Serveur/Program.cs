using Microsoft.Extensions.DependencyInjection.Extensions;
using Styles.Banque;
using Styles.Serveur;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ServiceBancaire>();
builder.Services.AddSingleton(s => ApiGraphQL.Moteur(s.GetRequiredService<ServiceBancaire>()));

var app = builder.Build();

app.MapRest();      // /clients, /comptes, /virements
app.MapGraphQL();   // /graphql
app.MapGrpc();      // /banque.Banque/…

app.MapGet("/", () => Results.Text("""
    La même banque, trois API :
      REST     http://localhost:5123/clients/alice, /clients/alice/comptes, /comptes/10001, /comptes/10001/operations, POST /virements
      GraphQL  POST http://localhost:5123/graphql ; schéma : GET /graphql/schema ; champs utilisés : GET /graphql/utilisation
      gRPC     http://localhost:5250 (HTTP/2), service banque.Banque, contrat : Styles.Contrats/banque.proto
    Le client de comparaison : dotnet run --project Styles.Client
    """, "text/plain; charset=utf-8"));

app.MapGet("/statistiques", (ServiceBancaire banque) => Results.Ok(new
{
    lecturesOperations = banque.Statistiques.LecturesOperations,
    abonnesAuFlux = banque.NombreAbonnes
}));

app.Run();

namespace Styles.Serveur
{
    /// <summary>Repère de l'assemblage, pour les tests (le client a lui aussi un Program).</summary>
    public sealed class Marqueur;
}
