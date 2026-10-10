using Mk.Hote;
using Mk.Noyau;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RegistrePlugins>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => Journal(sp));
builder.Services.AddSingleton(sp => new ChargeurPlugins(sp.GetRequiredService<RegistrePlugins>(), Journal(sp)));
builder.Services.AddSingleton(sp => new NoyauBancaire(
    sp.GetRequiredService<RegistrePlugins>(), sp.GetRequiredService<TimeProvider>(), Journal(sp)));
builder.Services.AddSingleton(sp => new DossierPlugins(Path.GetFullPath(Path.Combine(
    sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<IConfiguration>()["Plugins:Dossier"] ?? "../plugins"))));
builder.Services.AddHostedService<SurveillanceDesPlugins>();

var app = builder.Build();

// Chargement initial des plugins, puis comptes de démo.
app.Services.GetRequiredService<ChargeurPlugins>().Recharger(app.Services.GetRequiredService<DossierPlugins>().Chemin);
var noyau = app.Services.GetRequiredService<NoyauBancaire>();
noyau.Ouvrir(DonneesDeDemo.Alice, "Alice", 1_000m);
noyau.Ouvrir(DonneesDeDemo.Bob, "Bob", 500m);
noyau.Ouvrir(DonneesDeDemo.Carla, "Carla", 300m);

app.MapBanque();
app.MapAdministrationPlugins();

app.Run();

static Action<string> Journal(IServiceProvider sp)
{
    var journal = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Noyau");
    return message => journal.LogInformation("{Message}", message);
}

public partial class Program;
