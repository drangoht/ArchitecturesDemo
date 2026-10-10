using Micro.Passerelle;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("aval", client => client.Timeout = TimeSpan.FromSeconds(30));

var app = builder.Build();

// Table de routage : préfixe public → service. Tout ce qui n'y figure pas (ex. /interne/…) reste invisible.
var routes = app.Configuration.GetSection("Routes").Get<Dictionary<string, string>>() ?? [];

// Santé agrégée de tous les services : utile pour voir d'un coup d'œil qui est tombé.
app.MapGet("/sante", async (IHttpClientFactory http) =>
{
    var client = http.CreateClient("aval");
    var etats = new Dictionary<string, string>();
    foreach (var (prefixe, url) in routes)
    {
        try
        {
            var reponse = await client.GetAsync($"{url}/sante");
            etats[prefixe] = reponse.IsSuccessStatusCode ? "ok" : $"erreur {(int)reponse.StatusCode}";
        }
        catch (HttpRequestException)
        {
            etats[prefixe] = "injoignable";
        }
    }
    return Results.Ok(etats);
});

app.MapFallback((HttpContext contexte, IHttpClientFactory http) => Relais.Transmettre(contexte, routes, http));

app.Run();
