using System.Diagnostics;
using System.Text.Json.Serialization;
using Bff.Mobile;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Pas de champ null dans les réponses : chaque octet compte sur un réseau mobile.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddScoped<MesureAval>();
builder.Services.AddScoped<Ecrans>();
builder.Services.AddHttpClient<ClientServices>(c => c.BaseAddress = new Uri(builder.Configuration["Services:Url"]!));

var app = builder.Build();

app.Use(async (http, suite) =>
{
    var mesure = http.RequestServices.GetRequiredService<MesureAval>();
    var debut = Stopwatch.GetTimestamp();
    http.Response.OnStarting(() =>
    {
        http.Response.Headers["X-Appels-Aval"] = mesure.Appels.ToString();
        http.Response.Headers["X-Duree-Aval-Cumulee-Ms"] = ((int)mesure.DureeCumulee.TotalMilliseconds).ToString();
        http.Response.Headers["X-Duree-Totale-Ms"] = ((int)Stopwatch.GetElapsedTime(debut).TotalMilliseconds).ToString();
        return Task.CompletedTask;
    });
    try
    {
        await suite(http);
    }
    catch (ServiceAvalException e)
    {
        http.Response.StatusCode = e.Statut is 400 or 401 or 403 or 404 ? e.Statut : StatusCodes.Status502BadGateway;
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(e.Corps.StartsWith('{') ? e.Corps : """{"erreur":"Service indisponible."}""");
    }
});

// Une application mobile native garde son jeton dans le stockage sécurisé du téléphone (Keychain, Keystore) :
// pas de cookie ici, le BFF reçoit le jeton dans l'en-tête Authorization et le transmet aux services.
static string Jeton(HttpContext http)
{
    var entete = http.Request.Headers.Authorization.ToString();
    return entete.StartsWith("Bearer ") ? entete[7..] : throw new ServiceAvalException(401, """{"erreur":"Jeton d'accès requis."}""");
}

// ─── v1 : la version actuellement installée sur la plupart des téléphones ───
var v1 = app.MapGroup("/mobile/v1");

v1.MapPost("/connexion", async (Connexion c, ClientServices services) => Results.Ok(await services.ObtenirJeton(c.Utilisateur, c.MotDePasse)));

v1.MapGet("/accueil", async (HttpContext http, Ecrans ecrans) => Results.Ok(await ecrans.Accueil(Jeton(http), version: 1)));

v1.MapGet("/comptes/{id:guid}/operations", async (Guid id, string? curseur, HttpContext http, Ecrans ecrans) =>
    Results.Ok(await ecrans.Operations(id, curseur, Jeton(http))));

v1.MapPost("/virements", async (VirementRequete r, HttpContext http, ClientServices services) =>
    Results.Ok(await services.Virer(r.Source, r.Cible, r.Montant, Jeton(http))));

// ─── v2 : la nouvelle version de l'application ; seul l'accueil change ───
app.MapGet("/mobile/v2/accueil", async (HttpContext http, Ecrans ecrans) => Results.Ok(await ecrans.Accueil(Jeton(http), version: 2)));

app.Run();
