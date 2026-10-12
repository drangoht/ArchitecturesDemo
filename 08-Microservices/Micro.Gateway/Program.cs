using Micro.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("downstream", client => client.Timeout = TimeSpan.FromSeconds(30));

var app = builder.Build();

// Routing table: public prefix → service. Anything not listed (e.g. /internal/…) stays invisible.
var routes = app.Configuration.GetSection("Routes").Get<Dictionary<string, string>>() ?? [];

// Aggregated health of all the services: handy to see at a glance which one is down.
app.MapGet("/health", async (IHttpClientFactory http) =>
{
    var client = http.CreateClient("downstream");
    var states = new Dictionary<string, string>();
    foreach (var (prefix, url) in routes)
    {
        try
        {
            var response = await client.GetAsync($"{url}/health");
            states[prefix] = response.IsSuccessStatusCode ? "ok" : $"error {(int)response.StatusCode}";
        }
        catch (HttpRequestException)
        {
            states[prefix] = "unreachable";
        }
    }
    return Results.Ok(states);
});

app.MapFallback((HttpContext context, IHttpClientFactory http) => Relay.Forward(context, routes, http));

app.Run();
