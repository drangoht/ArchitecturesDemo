using Modulaire.Comptes;
using Modulaire.Notifications;
using Modulaire.Partage;
using Modulaire.Virements;

var builder = WebApplication.CreateBuilder(args);

// La liste des modules : retirer une ligne retire une fonctionnalité entière (routes, base, abonnements).
IModule[] modules = [new ComptesModule(), new VirementsModule(), new NotificationsModule()];

builder.Services.AddBusEvenements();
foreach (var module in modules)
    module.Enregistrer(builder.Services, builder.Configuration);

var app = builder.Build();

foreach (var module in modules)
{
    await module.Initialiser(app.Services);
    module.MapEndpoints(app);
}

app.MapGet("/", () => modules.Select(m => m.Nom));

app.Run();

// Rend Program visible pour WebApplicationFactory dans les tests.
public partial class Program;
