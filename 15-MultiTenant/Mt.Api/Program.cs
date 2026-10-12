using System.Text.Json.Serialization;
using Mt.Api;
using Mt.Bank;
using Mt.Tenants;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddTenants();
builder.Services.AddPerTenantRateLimiting();
builder.Services.AddBank();
builder.Services.AddSingleton<PlatformService>();

var app = builder.Build();

// The demo banks go through the same path as a new customer registered from the back office.
var platform = app.Services.GetRequiredService<PlatformService>();
foreach (var definition in app.Configuration.GetSection("Tenants").Get<List<TenantDefinition>>() ?? [])
    await platform.Register(definition, DemoData.Accounts(definition.Id));

// Business errors → HTTP, once for all the routes.
app.Use(async (http, next) =>
{
    try
    {
        await next(http);
    }
    catch (BusinessRuleException e)
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
    catch (AccountNotFoundException e)
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

// Order matters: first know WHO is calling, then apply THEIR quota.
app.UseTenants("/platform");
app.UseRateLimiter();

app.MapBank();
app.MapPlatform();

app.Run();

public partial class Program;
