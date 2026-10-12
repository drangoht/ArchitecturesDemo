using Modular.Accounts;
using Modular.Notifications;
using Modular.Shared;
using Modular.Transfers;

var builder = WebApplication.CreateBuilder(args);

// The list of modules: removing a line removes a whole feature (routes, database, subscriptions).
IModule[] modules = [new AccountsModule(), new TransfersModule(), new NotificationsModule()];

builder.Services.AddEventBus();
foreach (var module in modules)
    module.Register(builder.Services, builder.Configuration);

var app = builder.Build();

foreach (var module in modules)
{
    await module.Initialize(app.Services);
    module.MapEndpoints(app);
}

app.MapGet("/", () => modules.Select(m => m.Name));

app.Run();

// Makes Program visible to WebApplicationFactory in the tests.
public partial class Program;
