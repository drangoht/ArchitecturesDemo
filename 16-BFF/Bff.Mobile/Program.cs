using System.Diagnostics;
using System.Text.Json.Serialization;
using Bff.Mobile;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// No null field in the responses: every byte counts on a mobile network.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddScoped<DownstreamMetrics>();
builder.Services.AddScoped<Screens>();
builder.Services.AddHttpClient<ServicesClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:Url"]!));

var app = builder.Build();

app.Use(async (http, next) =>
{
    var metrics = http.RequestServices.GetRequiredService<DownstreamMetrics>();
    var start = Stopwatch.GetTimestamp();
    http.Response.OnStarting(() =>
    {
        http.Response.Headers["X-Downstream-Calls"] = metrics.Calls.ToString();
        http.Response.Headers["X-Downstream-Cumulative-Ms"] = ((int)metrics.CumulativeDuration.TotalMilliseconds).ToString();
        http.Response.Headers["X-Total-Duration-Ms"] = ((int)Stopwatch.GetElapsedTime(start).TotalMilliseconds).ToString();
        return Task.CompletedTask;
    });
    try
    {
        await next(http);
    }
    catch (DownstreamServiceException e)
    {
        http.Response.StatusCode = e.Status is 400 or 401 or 403 or 404 ? e.Status : StatusCodes.Status502BadGateway;
        http.Response.ContentType = "application/json; charset=utf-8";
        await http.Response.WriteAsync(e.Body.StartsWith('{') ? e.Body : """{"error":"Service unavailable."}""");
    }
});

// A native mobile application keeps its token in the phone's secure storage (Keychain, Keystore):
// no cookie here, the BFF receives the token in the Authorization header and passes it on to the services.
static string Token(HttpContext http)
{
    var header = http.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ") ? header[7..] : throw new DownstreamServiceException(401, """{"error":"Access token required."}""");
}

// ─── v1: the version currently installed on most phones ───
var v1 = app.MapGroup("/mobile/v1");

v1.MapPost("/login", async (Login l, ServicesClient services) => Results.Ok(await services.GetToken(l.User, l.Password)));

v1.MapGet("/home", async (HttpContext http, Screens screens) => Results.Ok(await screens.Home(Token(http), version: 1)));

v1.MapGet("/accounts/{id:guid}/operations", async (Guid id, string? cursor, HttpContext http, Screens screens) =>
    Results.Ok(await screens.Operations(id, cursor, Token(http))));

v1.MapPost("/transfers", async (TransferRequest r, HttpContext http, ServicesClient services) =>
    Results.Ok(await services.Transfer(r.Source, r.Target, r.Amount, Token(http))));

// ─── v2: the new version of the application; only the home screen changes ───
app.MapGet("/mobile/v2/home", async (HttpContext http, Screens screens) => Results.Ok(await screens.Home(Token(http), version: 2)));

app.Run();
