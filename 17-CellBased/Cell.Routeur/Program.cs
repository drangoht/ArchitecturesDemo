using Cell.Routeur;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new RegistreCellules(builder.Configuration.GetSection("Cellules").Get<List<Cellule>>() ?? []));
builder.Services.AddSingleton<TablePlacement>();
builder.Services.AddSingleton<Relais>();
builder.Services.AddSingleton<PlanDeControle>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AmorcageDemo>();

var app = builder.Build();

app.Use(async (http, suite) =>
{
    try
    {
        await suite(http);
    }
    catch (PlanException e)
    {
        http.Response.StatusCode = e.Statut;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (HttpRequestException e)
    {
        http.Response.StatusCode = StatusCodes.Status502BadGateway;
        await http.Response.WriteAsJsonAsync(new { erreur = $"Cellule injoignable : {e.Message}" });
    }
});

// ─── Plan de données : tout le trafic des clients, relayé vers LA cellule du client ───
app.Map("/clients/{client}/{**reste}", (string client, HttpContext http, Relais relais) => relais.Relayer(client, http));

// Les cellules s'en servent pour créditer un client d'une autre cellule (en production : un point d'entrée interne, non exposé).
app.MapPost("/interne/clients/{client}/credits", (string client, HttpContext http, Relais relais) => relais.Relayer(client, http));

// Nouveau client : c'est le plan de contrôle qui choisit sa cellule.
app.MapPost("/clients", async (NouveauClient n, PlanDeControle plan) =>
{
    var cellule = await plan.CreerClient(new ClientExport(n.Id, n.Nom,
        [new CompteExport(Guid.NewGuid(), "Compte courant", n.DepotInitial)], [], []));
    return Results.Created($"/clients/{n.Id}/comptes", new { client = n.Id, cellule = cellule.Id });
});

// ─── Plan de contrôle ───
var plateforme = app.MapGroup("/plateforme");

plateforme.MapGet("/cellules", async (PlanDeControle plan) => Results.Ok(await plan.Etat()));

plateforme.MapGet("/placement", (TablePlacement placement) => Results.Ok(placement.Tout()));

plateforme.MapPost("/cellules", async (Cellule cellule, PlanDeControle plan) =>
    Results.Created($"/plateforme/cellules/{cellule.Id}", await plan.Enregistrer(cellule)));

plateforme.MapPost("/cellules/{id}/fermer", (string id, PlanDeControle plan) => plan.Fermer(id) ? Results.NoContent() : Results.NotFound());

plateforme.MapPost("/clients/{client}/migrer", async (string client, Destination d, PlanDeControle plan) =>
    Results.Ok(await plan.Migrer(client, d.Vers)));

plateforme.MapPost("/deploiements", async (Deploiement d, PlanDeControle plan) => Results.Ok(await plan.Deployer(d.Version)));

app.Run();

namespace Cell.Routeur
{
    public sealed record NouveauClient(string Id, string Nom, decimal DepotInitial = 0);
    public sealed record Destination(string Vers);
    public sealed record Deploiement(string Version);
}
