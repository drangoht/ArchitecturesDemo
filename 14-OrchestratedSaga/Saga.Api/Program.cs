using Saga.Api;
using Saga.Orchestrator;
using Saga.Participants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Saga").Get<SagaOptions>() ?? new SagaOptions());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Chaos>();
builder.Services.AddSingleton<AccountsService>();
builder.Services.AddSingleton<ComplianceService>();
builder.Services.AddSingleton<ExchangeService>();
builder.Services.AddSingleton<NotificationsService>();
builder.Services.AddSingleton<SagaStore>();
builder.Services.AddSingleton<TransferDefinition>();
builder.Services.AddSingleton<SagaEngine>();
builder.Services.AddSingleton<OrchestratorLoop>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OrchestratorLoop>());

var app = builder.Build();

var accounts = app.Services.GetRequiredService<AccountsService>();
accounts.Open(DemoData.Alice, "Alice", "EUR", 50_000m);
accounts.Open(DemoData.Bob, "Bob", "EUR", 500m);
accounts.Open(DemoData.Carla, "Carla", "USD", 1_000m);

app.MapSagas();

app.Run();

public partial class Program;
