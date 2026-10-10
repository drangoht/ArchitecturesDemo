using System.Text.Json.Serialization;
using Mt.Api;
using Mt.Banque;
using Mt.Locataires;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddLocataires();
builder.Services.AddLimitationParLocataire();
builder.Services.AddBanque();
builder.Services.AddSingleton<ServicePlateforme>();

var app = builder.Build();

// Les banques de démo passent par le même chemin qu'un nouveau client inscrit depuis le back-office.
var plateforme = app.Services.GetRequiredService<ServicePlateforme>();
foreach (var definition in app.Configuration.GetSection("Locataires").Get<List<DefinitionLocataire>>() ?? [])
    await plateforme.Inscrire(definition, DonneesDeDemo.Comptes(definition.Id));

// Erreurs métier → HTTP, une seule fois pour toutes les routes.
app.Use(async (http, suite) =>
{
    try
    {
        await suite(http);
    }
    catch (RegleMetierException e)
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (CompteIntrouvableException e)
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

// L'ordre compte : d'abord savoir QUI appelle, ensuite appliquer SON quota.
app.UseLocataires("/plateforme");
app.UseRateLimiter();

app.MapBanque();
app.MapPlateforme();

app.Run();

public partial class Program;
