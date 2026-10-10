using Hexa.Adapters.Notifications;
using Hexa.Adapters.Persistance;
using Hexa.Adapters.Web;
using Hexa.Hexagone.Application;
using Hexa.Hexagone.Ports.Entrants;
using Hexa.Hexagone.Ports.Sortants;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// ── Composition root : c'est ici, et seulement ici, qu'on choisit les adapters ──

// Adapters sortants, interchangeables par configuration : --Persistance=Json ou Memoire (défaut).
builder.Services.AddSingleton<IComptes>(_ => builder.Configuration["Persistance"] switch
{
    "Json" => new ComptesFichierJson(builder.Configuration["FichierComptes"] ?? "comptes.json", DonneesDeDemo.Comptes()),
    _ => new ComptesEnMemoire(DonneesDeDemo.Comptes())
});
builder.Services.AddSingleton<INotifications>(new NotificationsJournal(Console.Out));

// Ports entrants → leur implémentation dans l'hexagone.
builder.Services.AddScoped<IConsulterCompte, ConsulterCompte>();
builder.Services.AddScoped<IRetirerArgent, RetirerArgent>();
builder.Services.AddScoped<IEffectuerVirement, EffectuerVirement>();

var app = builder.Build();

app.MapComptesEndpoints();

app.Run();
