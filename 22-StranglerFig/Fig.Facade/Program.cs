using System.Text.Json.Serialization;
using Fig.Facade;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Facade").Get<OptionsFacade>() ?? new OptionsFacade();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TableDeRoutage>();
builder.Services.AddSingleton<Comparateur>();
builder.Services.AddSingleton<Relais>();
builder.Services.AddHttpClient("ancien", c => c.BaseAddress = new Uri(options.Ancien));
builder.Services.AddHttpClient("nouveau", c => c.BaseAddress = new Uri(options.Nouveau));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// ─── Le pilotage de la migration (réservé à l'équipe : en-tête X-Cle-Admin) ───
var facade = app.MapGroup("/facade").AddEndpointFilter(async (contexte, suite) =>
    contexte.HttpContext.Request.Headers["X-Cle-Admin"] == options.CleAdmin ? await suite(contexte) : Results.Unauthorized());

facade.MapGet("/routes", (TableDeRoutage table) => Results.Ok(table.Etat()));

// Basculer une route, à chaud, dans un sens comme dans l'autre : migrer, ou revenir en arrière.
facade.MapPut("/routes/{nom}", (string nom, ChangementDeMode changement, TableDeRoutage table, ILogger<Relais> journal) =>
{
    try
    {
        var avant = table.Basculer(nom, changement.Mode);
        journal.LogWarning("Route {Route} : {Avant} → {Apres}", nom, avant, changement.Mode);
        return Results.Ok(table.Etat().Single(r => r.Nom == nom));
    }
    catch (RouteInconnueException e)
    {
        return Results.NotFound(new { erreur = e.Message });
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { erreur = e.Message });
    }
});

facade.MapGet("/progression", (TableDeRoutage table) => Results.Ok(table.Progression()));

facade.MapGet("/comparaisons", (Comparateur comparateur) => Results.Ok(comparateur.Bilan()));

// ─── Tout le reste : relayé vers l'ancien ou le nouveau, selon la table ───
app.Map("/{**chemin}", (HttpContext http, Relais relais) => relais.Traiter(http));

app.Run();

public sealed record ChangementDeMode(Mode Mode);
