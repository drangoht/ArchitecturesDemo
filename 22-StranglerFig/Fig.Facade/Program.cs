using System.Text.Json.Serialization;
using Fig.Facade;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Facade").Get<FacadeOptions>() ?? new FacadeOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<RoutingTable>();
builder.Services.AddSingleton<ShadowComparer>();
builder.Services.AddSingleton<Relay>();
builder.Services.AddHttpClient("legacy", c => c.BaseAddress = new Uri(options.Legacy));
builder.Services.AddHttpClient("modern", c => c.BaseAddress = new Uri(options.Modern));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// ─── Steering the migration (reserved for the team: X-Admin-Key header) ───
var facade = app.MapGroup("/facade").AddEndpointFilter(async (context, next) =>
    context.HttpContext.Request.Headers["X-Admin-Key"] == options.AdminKey ? await next(context) : Results.Unauthorized());

facade.MapGet("/routes", (RoutingTable table) => Results.Ok(table.State()));

// Switch a route, live, one way or the other: migrate, or roll back.
facade.MapPut("/routes/{name}", (string name, ModeChange change, RoutingTable table, ILogger<Relay> logger) =>
{
    try
    {
        var before = table.Switch(name, change.Mode);
        logger.LogWarning("Route {Route}: {Before} → {After}", name, before, change.Mode);
        return Results.Ok(table.State().Single(r => r.Name == name));
    }
    catch (UnknownRouteException e)
    {
        return Results.NotFound(new { error = e.Message });
    }
    catch (ArgumentException e)
    {
        return Results.BadRequest(new { error = e.Message });
    }
});

facade.MapGet("/progress", (RoutingTable table) => Results.Ok(table.Progress()));

facade.MapGet("/comparisons", (ShadowComparer comparer) => Results.Ok(comparer.Report()));

// ─── Everything else: relayed to the legacy or the modern system, according to the table ───
app.Map("/{**path}", (HttpContext http, Relay relay) => relay.Handle(http));

app.Run();

public sealed record ModeChange(Mode Mode);
