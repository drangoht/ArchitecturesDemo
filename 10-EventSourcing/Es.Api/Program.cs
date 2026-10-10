using Es.Api;
using Es.Domaine;
using Es.Projections;
using Es.Stockage;

var builder = WebApplication.CreateBuilder(args);

// Stockage:Fichier vide → magasin en mémoire (utilisé par les tests).
var fichier = builder.Configuration["Stockage:Fichier"];
var cheminFichier = string.IsNullOrWhiteSpace(fichier) ? null : Path.Combine(builder.Environment.ContentRootPath, fichier);

builder.Services.AddSingleton<IMagasinEvenements>(new MagasinEvenements(cheminFichier));
builder.Services.AddSingleton<MagasinInstantanes>();
builder.Services.AddScoped<DepotComptes>();
builder.Services.AddSingleton<ProjectionReleves>();
builder.Services.AddSingleton<ProjectionPlusGrosRetraits>();
builder.Services.AddSingleton<MoteurProjections>();
builder.Services.AddHostedService<AbonnementProjections>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Premier démarrage (magasin vide) : on crée les comptes de démo… par des événements, évidemment.
if ((await app.Services.GetRequiredService<IMagasinEvenements>().LireTout(maximum: 1)).Count == 0)
{
    using var scope = app.Services.CreateScope();
    var maintenant = DateTime.UtcNow;
    await scope.ServiceProvider.GetRequiredService<DepotComptes>().Enregistrer(
        Compte.Ouvrir(DonneesDeDemo.Alice, "Alice", 1000m, maintenant),
        Compte.Ouvrir(DonneesDeDemo.Bob, "Bob", 500m, maintenant));
}

app.Use(async (contexte, suivant) =>
{
    try
    {
        await suivant(contexte);
    }
    catch (RegleMetierException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status400BadRequest;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (CompteIntrouvableException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
    catch (ConflitDeConcurrenceException e)
    {
        contexte.Response.StatusCode = StatusCodes.Status409Conflict;
        await contexte.Response.WriteAsJsonAsync(new { erreur = e.Message });
    }
});

app.MapCommandes();
app.MapLectures();

app.Run();

public partial class Program;
