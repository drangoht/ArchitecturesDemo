using System.Text.Json.Serialization;
using Acteurs.Api;
using Acteurs.Banque;
using Acteurs.Noyau;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<OptionsActeurs>(builder.Configuration.GetSection("Acteurs"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MagasinEtats>();
builder.Services.AddSingleton<Pieges>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OptionsActeurs>>().Value;
    var horloge = sp.GetRequiredService<TimeProvider>();
    return new SystemeActeurs(horloge) { DelaiDemandes = TimeSpan.FromMilliseconds(options.DelaiDemandeMs) }.InstallerBanque(sp.GetRequiredService<MagasinEtats>(), sp.GetRequiredService<Pieges>(),
        horloge, new StrategieSupervision(options.MaxRedemarrages, TimeSpan.FromSeconds(options.FenetreSecondes)));
});
builder.Services.AddHostedService<PassivationAutomatique>();

var app = builder.Build();

// Les comptes de démo sont ouverts comme les autres : par un message à leur acteur.
var systeme = app.Services.GetRequiredService<SystemeActeurs>();
await systeme.Compte(DonneesDeDemo.Alice).Demander(new OuvrirCompte("Alice", 1_000m));
await systeme.Compte(DonneesDeDemo.Bob).Demander(new OuvrirCompte("Bob", 500m));

// Ce que le runtime signale → HTTP, une seule fois pour toutes les routes.
app.Use(async (http, suite) =>
{
    try
    {
        await suite(http);
    }
    catch (ActeurEnPanneException e)
    {
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message, acteur = e.Chemin, suite = "l'acteur a été redémarré ; la requête peut être rejouée" });
    }
    catch (ActeurIndisponibleException e)
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message, acteur = e.Chemin });
    }
    catch (TimeoutException e)
    {
        http.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

app.MapBanque();
app.MapSysteme();
app.MapDemonstrations();

app.Run();

public partial class Program;
