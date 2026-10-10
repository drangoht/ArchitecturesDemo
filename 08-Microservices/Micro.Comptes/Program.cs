using Micro.Comptes;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ComptesDb>((sp, options) =>
    options.UseInMemoryDatabase(sp.GetRequiredService<IConfiguration>()["NomBase"] ?? "Comptes"));
builder.Services.AddHttpClient("courtier", (sp, client) =>
    client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Courtier:Url"]!));
builder.Services.AddHostedService<RelaisOutbox>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<ComptesDb>().InitialiserDonneesDeDemo();

app.MapGet("/sante", () => Results.Ok(new { service = "comptes", statut = "ok" }));
app.MapEndpointsPublics();
app.MapEndpointsInternes();

app.Run();
