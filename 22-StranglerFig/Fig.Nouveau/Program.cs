using Fig.Nouveau;
using Microsoft.Extensions.DependencyInjection.Extensions;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Depot>();
builder.Services.AddSingleton<Synchronisation>();
builder.Services.AddHostedService<SynchronisationPeriodique>();
builder.Services.AddHttpClient<ClientAncien>(c => c.BaseAddress = new Uri(builder.Configuration["Ancien:Url"]!));

var app = builder.Build();

app.Use(async (http, suite) =>
{
    try
    {
        await suite(http);
    }
    catch (ErreurMetier e)
    {
        http.Response.StatusCode = e.Statut;
        await http.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

// ─── Le contrat historique, reproduit route par route ───

app.MapGet("/comptes/{numero}", (string numero, Depot depot) =>
    Results.Ok(depot.Lire(numero, Contrat.Compte)));

app.MapGet("/comptes/{numero}/operations", (string numero, Depot depot) =>
    Results.Ok(depot.Lire(numero, Contrat.Operations)));

app.MapGet("/comptes/{numero}/releve", (string numero, string? mois, Depot depot, TimeProvider horloge) =>
{
    var periode = Contrat.Periode(mois, horloge.GetUtcNow());
    return Results.Text(depot.Lire(numero, c => Contrat.Releve(c, periode)), "text/plain; charset=utf-8");
});

app.MapPost("/virements", (DemandeVirement d, Depot depot, TimeProvider horloge) =>
{
    var maintenant = horloge.GetUtcNow();
    maintenant = maintenant.AddTicks(-(maintenant.Ticks % TimeSpan.TicksPerSecond));   // la seconde : précision du contrat
    return Results.Ok(Contrat.Virement(depot.Virer(d.Source, d.Cible, d.Montant, maintenant)));
});

// ─── Pour l'exploitation (non relayé par la façade) ───

app.MapPost("/interne/synchroniser", async (Synchronisation synchronisation, CancellationToken annulation) =>
    Results.Ok(await synchronisation.Executer(annulation)));

app.MapGet("/interne/etat", (Depot depot) => Results.Ok(new
{
    positionAncien = depot.PositionAncien,
    aReporter = depot.AReporter().Count,
    comptes = depot.Numeros()
}));

app.Run();
