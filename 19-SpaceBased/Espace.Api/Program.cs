using System.Text.Json.Serialization;
using Espace.Api;
using Espace.Banque;
using Espace.Grille;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<OptionsElasticite>(builder.Configuration.GetSection("Elasticite"));
var options = builder.Configuration.GetSection("Plateforme").Get<OptionsPlateforme>() ?? new OptionsPlateforme();
builder.Services.AddSingleton(_ => new Plateforme(options, Traitement.Traiter));  // fabrique : le conteneur la libère à l'arrêt
builder.Services.AddHostedService<ElasticiteAutomatique>();

var app = builder.Build();

// La base contient Alice et Bob ; les premières unités chargent la grille depuis elle.
await app.Services.GetRequiredService<Plateforme>().Demarrer(DonneesDeDemo.Comptes());

// Ce que signale l'intergiciel → HTTP, une seule fois pour toutes les routes.
app.Use(async (http, suite) =>
{
    try
    {
        await suite(http);
    }
    catch (Exception e) when (e is AucuneUniteException or BaseIndisponibleException or DemarrageAFroidImpossibleException)
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

app.MapBanque();
app.MapGrille();
app.MapDemonstrations();

app.Run();

public partial class Program;
