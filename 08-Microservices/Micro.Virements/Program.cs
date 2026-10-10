using Micro.Virements;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<VirementsDb>((sp, options) =>
    options.UseInMemoryDatabase(sp.GetRequiredService<IConfiguration>()["NomBase"] ?? "Virements"));

// Appels au service Comptes : le réseau peut échouer, on se protège.
// Retry (y compris sur POST : sans danger, car les appels portent un operationId idempotent),
// timeout par tentative et circuit breaker pour ne pas insister sur un service à terre.
builder.Services.AddHttpClient("comptes", (sp, client) =>
        client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Services:Comptes"]!))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });
builder.Services.AddScoped<ClientComptes>();

builder.Services.AddHttpClient("courtier", (sp, client) =>
    client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Courtier:Url"]!));
builder.Services.AddHostedService<RelaisOutbox>();

var app = builder.Build();

app.MapGet("/sante", () => Results.Ok(new { service = "virements", statut = "ok" }));
app.MapVirements();

app.Run();
