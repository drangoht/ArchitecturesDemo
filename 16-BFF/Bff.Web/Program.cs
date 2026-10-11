using System.Diagnostics;
using System.Security.Claims;
using Bff.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSessionBff();
builder.Services.AddScoped<MesureAval>();
builder.Services.AddScoped<TableauDeBord>();
builder.Services.AddHttpClient<ClientServices>(c => c.BaseAddress = new Uri(builder.Configuration["Services:Url"]!));

var app = builder.Build();

// En-têtes de mesure : combien d'appels aux services cette réponse a coûté, et combien de temps.
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
    await suite(http);
});

// Les erreurs des services, traduites pour le navigateur.
app.Use(async (http, suite) =>
{
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

app.UseDefaultFiles();
app.UseStaticFiles();   // l'application web (wwwroot) est servie par SON BFF : même origine, donc pas de CORS
app.UseAuthentication();
app.UseAuthorization();

// ─── Session : connexion, déconnexion, utilisateur courant ───
var bff = app.MapGroup("/bff").AddEndpointFilter<ExigerEnTeteAntiCsrf>();

bff.MapPost("/connexion", async (Connexion c, ClientServices services, HttpContext http) =>
{
    var jeton = await services.ObtenirJeton(c.Utilisateur, c.MotDePasse);
    var proprietes = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(jeton.ExpireDans) };
    proprietes.StoreTokens([new AuthenticationToken { Name = "access_token", Value = jeton.Jeton }]);
    var identite = new ClaimsIdentity([new Claim(ClaimTypes.Name, c.Utilisateur)], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identite), proprietes);
    // La réponse ne contient AUCUN jeton : le navigateur ne reçoit qu'un cookie de session HttpOnly.
    return Results.Ok(new { utilisateur = c.Utilisateur });
});

bff.MapPost("/deconnexion", async (HttpContext http) =>
{
    await http.SignOutAsync();   // supprime aussi la session côté serveur
    return Results.NoContent();
}).RequireAuthorization();

bff.MapGet("/utilisateur", (ClaimsPrincipal utilisateur) => Results.Ok(new { nom = utilisateur.Identity!.Name }))
    .RequireAuthorization();

// ─── L'API taillée pour l'écran du site ───
var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<ExigerEnTeteAntiCsrf>();

api.MapGet("/tableau-de-bord", async (HttpContext http, TableauDeBord tableau) =>
    Results.Ok(await tableau.Construire(await Jeton(http))));

api.MapPost("/virements", async (VirementRequete r, HttpContext http, ClientServices services) =>
    Results.Ok(await services.Virer(r.Source, r.Cible, r.Montant, await Jeton(http))));

app.Run();

// Le jeton est lu dans la session du serveur, jamais dans la requête du navigateur.
static async Task<string> Jeton(HttpContext http) =>
    await http.GetTokenAsync("access_token") ?? throw new ServiceAvalException(401, """{"erreur":"Session sans jeton."}""");
