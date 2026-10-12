using System.Diagnostics;
using System.Security.Claims;
using Bff.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBffSession();
builder.Services.AddScoped<DownstreamMetrics>();
builder.Services.AddScoped<Dashboard>();
builder.Services.AddHttpClient<ServicesClient>(c => c.BaseAddress = new Uri(builder.Configuration["Services:Url"]!));

var app = builder.Build();

// Measurement headers: how many calls to the services this response cost, and how much time.
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
    await next(http);
});

// The errors of the services, translated for the browser.
app.Use(async (http, next) =>
{
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

app.UseDefaultFiles();
app.UseStaticFiles();   // the web application (wwwroot) is served by ITS BFF: same origin, hence no CORS
app.UseAuthentication();
app.UseAuthorization();

// ─── Session: login, logout, current user ───
var bff = app.MapGroup("/bff").AddEndpointFilter<RequireAntiCsrfHeader>();

bff.MapPost("/login", async (Login l, ServicesClient services, HttpContext http) =>
{
    var token = await services.GetToken(l.User, l.Password);
    var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn) };
    properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token.Token }]);
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, l.User)], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identity), properties);
    // The response contains NO token: the browser only receives an HttpOnly session cookie.
    return Results.Ok(new { user = l.User });
});

bff.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync();   // also deletes the session on the server side
    return Results.NoContent();
}).RequireAuthorization();

bff.MapGet("/user", (ClaimsPrincipal user) => Results.Ok(new { name = user.Identity!.Name }))
    .RequireAuthorization();

// ─── The API cut out for the site's screen ───
var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<RequireAntiCsrfHeader>();

api.MapGet("/dashboard", async (HttpContext http, Dashboard dashboard) =>
    Results.Ok(await dashboard.Build(await Token(http))));

api.MapPost("/transfers", async (TransferRequest r, HttpContext http, ServicesClient services) =>
    Results.Ok(await services.Transfer(r.Source, r.Target, r.Amount, await Token(http))));

app.Run();

// The token is read from the server's session, never from the browser's request.
static async Task<string> Token(HttpContext http) =>
    await http.GetTokenAsync("access_token") ?? throw new DownstreamServiceException(401, """{"error":"Session without a token."}""");
