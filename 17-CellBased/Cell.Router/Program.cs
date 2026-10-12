using Cell.Router;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new CellRegistry(builder.Configuration.GetSection("Cells").Get<List<CellInfo>>() ?? []));
builder.Services.AddSingleton<PlacementTable>();
builder.Services.AddSingleton<Relay>();
builder.Services.AddSingleton<ControlPlane>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<DemoSeeding>();

var app = builder.Build();

app.Use(async (http, next) =>
{
    try
    {
        await next(http);
    }
    catch (ControlPlaneException e)
    {
        http.Response.StatusCode = e.Status;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
    catch (HttpRequestException e)
    {
        http.Response.StatusCode = StatusCodes.Status502BadGateway;
        await http.Response.WriteAsJsonAsync(new { error = $"Cell unreachable: {e.Message}" });
    }
});

// ─── Data plane: all the customers' traffic, relayed to THE customer's cell ───
app.Map("/customers/{customer}/{**rest}", (string customer, HttpContext http, Relay relay) => relay.Forward(customer, http));

// The cells use it to credit a customer of another cell (in production: an internal entry point, not exposed).
app.MapPost("/internal/customers/{customer}/credits", (string customer, HttpContext http, Relay relay) => relay.Forward(customer, http));

// New customer: it is the control plane that picks its cell.
app.MapPost("/customers", async (NewCustomer n, ControlPlane plane) =>
{
    var cell = await plane.CreateCustomer(new CustomerExport(n.Id, n.Name,
        [new AccountExport(Guid.NewGuid(), "Checking account", n.InitialDeposit)], [], []));
    return Results.Created($"/customers/{n.Id}/accounts", new { customer = n.Id, cell = cell.Id });
});

// ─── Control plane ───
var platform = app.MapGroup("/platform");

platform.MapGet("/cells", async (ControlPlane plane) => Results.Ok(await plane.State()));

platform.MapGet("/placement", (PlacementTable placement) => Results.Ok(placement.All()));

platform.MapPost("/cells", async (CellInfo cell, ControlPlane plane) =>
    Results.Created($"/platform/cells/{cell.Id}", await plane.Register(cell)));

platform.MapPost("/cells/{id}/close", (string id, ControlPlane plane) => plane.Close(id) ? Results.NoContent() : Results.NotFound());

platform.MapPost("/customers/{customer}/migrate", async (string customer, Destination d, ControlPlane plane) =>
    Results.Ok(await plane.Migrate(customer, d.To)));

platform.MapPost("/deployments", async (Deployment d, ControlPlane plane) => Results.Ok(await plane.Deploy(d.Version)));

app.Run();

namespace Cell.Router
{
    public sealed record NewCustomer(string Id, string Name, decimal InitialDeposit = 0);
    public sealed record Destination(string To);
    public sealed record Deployment(string Version);
}
