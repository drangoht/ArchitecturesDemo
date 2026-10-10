using Saga.Api;
using Saga.Orchestrateur;
using Saga.Participants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Saga").Get<OptionsSaga>() ?? new OptionsSaga());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Chaos>();
builder.Services.AddSingleton<ServiceComptes>();
builder.Services.AddSingleton<ServiceConformite>();
builder.Services.AddSingleton<ServiceChange>();
builder.Services.AddSingleton<ServiceNotifications>();
builder.Services.AddSingleton<MagasinSagas>();
builder.Services.AddSingleton<DefinitionVirement>();
builder.Services.AddSingleton<MoteurSaga>();
builder.Services.AddSingleton<BoucleOrchestrateur>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BoucleOrchestrateur>());

var app = builder.Build();

var comptes = app.Services.GetRequiredService<ServiceComptes>();
comptes.Ouvrir(DonneesDeDemo.Alice, "Alice", "EUR", 50_000m);
comptes.Ouvrir(DonneesDeDemo.Bob, "Bob", "EUR", 500m);
comptes.Ouvrir(DonneesDeDemo.Carla, "Carla", "USD", 1_000m);

app.MapSagas();

app.Run();

public partial class Program;
