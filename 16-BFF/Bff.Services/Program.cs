using Bff.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Banque>();
builder.Services.AddSingleton<Jetons>();
builder.Services.AddSingleton(new Chaos { LatenceMs = builder.Configuration.GetValue("Services:LatenceMs", 120) });

var app = builder.Build();

// Chaque appel coûte un aller-retour réseau et un peu de traitement : on le simule, pour que l'agrégation se mesure.
app.Use(async (http, suite) =>
{
    if (!http.Request.Path.StartsWithSegments("/chaos"))
        await Task.Delay(http.RequestServices.GetRequiredService<Chaos>().LatenceMs);
    await suite(http);
});

// Toutes les API métier exigent un jeton valide ; elles vérifient aussi que la donnée appartient à l'utilisateur.
// Le BFF n'a donc AUCUN passe-droit : il agit avec le jeton de l'utilisateur, jamais avec le sien.
app.Use(async (http, suite) =>
{
    if (http.Request.Path.StartsWithSegments("/identite") || http.Request.Path.StartsWithSegments("/chaos"))
    {
        await suite(http);
        return;
    }
    var entete = http.Request.Headers.Authorization.ToString();
    var utilisateur = entete.StartsWith("Bearer ") ? http.RequestServices.GetRequiredService<Jetons>().Valider(entete[7..]) : null;
    if (utilisateur is null)
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await http.Response.WriteAsJsonAsync(new { erreur = "Jeton d'accès absent, invalide ou expiré." });
        return;
    }
    http.Items["utilisateur"] = utilisateur;
    await suite(http);
});

app.MapServices();

app.Run();
