using Bff.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Bank>();
builder.Services.AddSingleton<Tokens>();
builder.Services.AddSingleton(new Chaos { LatencyMs = builder.Configuration.GetValue("Services:LatencyMs", 120) });

var app = builder.Build();

// Each call costs a network round trip and a bit of processing: it is simulated, so that the aggregation can be measured.
app.Use(async (http, next) =>
{
    if (!http.Request.Path.StartsWithSegments("/chaos"))
        await Task.Delay(http.RequestServices.GetRequiredService<Chaos>().LatencyMs);
    await next(http);
});

// All the business APIs require a valid token; they also check that the data belongs to the user.
// The BFF therefore has NO special privilege: it acts with the user's token, never with its own.
app.Use(async (http, next) =>
{
    if (http.Request.Path.StartsWithSegments("/identity") || http.Request.Path.StartsWithSegments("/chaos"))
    {
        await next(http);
        return;
    }
    var header = http.Request.Headers.Authorization.ToString();
    var user = header.StartsWith("Bearer ") ? http.RequestServices.GetRequiredService<Tokens>().Validate(header[7..]) : null;
    if (user is null)
    {
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await http.Response.WriteAsJsonAsync(new { error = "Access token missing, invalid or expired." });
        return;
    }
    http.Items["user"] = user;
    await next(http);
});

app.MapServices();

app.Run();
