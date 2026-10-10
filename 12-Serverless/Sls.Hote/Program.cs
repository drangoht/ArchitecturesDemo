using Sls.Fonctions;
using Sls.Plateforme;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlateformeServerless(Deploiement.Declarer);

var app = builder.Build();

DonneesDeDemo.Charger(app.Services.GetRequiredService<ServicesGeres>());

app.MapGet("/", () => Results.Redirect("/plateforme/fonctions"));
app.MapPlateformeServerless();

app.Run();

public partial class Program;
