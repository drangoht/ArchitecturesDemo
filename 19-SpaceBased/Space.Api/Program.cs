using System.Text.Json.Serialization;
using Space.Api;
using Space.Bank;
using Space.Grid;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ElasticityOptions>(builder.Configuration.GetSection("Elasticity"));
var options = builder.Configuration.GetSection("Platform").Get<PlatformOptions>() ?? new PlatformOptions();
builder.Services.AddSingleton(_ => new Platform(options, Processing.Process));  // factory: the container disposes of it on shutdown
builder.Services.AddHostedService<AutomaticElasticity>();

var app = builder.Build();

// The database contains Alice and Bob; the first units load the grid from it.
await app.Services.GetRequiredService<Platform>().Start(DemoData.Accounts());

// What the middleware reports → HTTP, once for all the routes.
app.Use(async (http, next) =>
{
    try
    {
        await next(http);
    }
    catch (Exception e) when (e is NoUnitException or DatabaseUnavailableException or ColdStartImpossibleException)
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

app.MapBank();
app.MapGrid();
app.MapDemonstrations();

app.Run();

public partial class Program;
