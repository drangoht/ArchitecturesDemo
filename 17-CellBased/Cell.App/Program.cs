using System.Net.Http.Json;
using Cell.App;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

var cellId = builder.Configuration["Cell:Id"] ?? "c1";
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<CellState>();
builder.Services.AddSingleton(new CellVersion(builder.Configuration["Cell:Version"] ?? "1.0"));
// For transfers to a customer of another cell: the cell does not know the other cells, it goes through the router.
builder.Services.AddHttpClient("router", c => c.BaseAddress = new Uri(builder.Configuration["Router:Url"] ?? "http://localhost:5117"));

var app = builder.Build();

app.Use(async (http, next) =>
{
    http.Response.Headers["X-Cell"] = cellId;
    http.Response.Headers["X-Cell-Version"] = http.RequestServices.GetRequiredService<CellVersion>().Current;
    if (http.RequestServices.GetRequiredService<CellState>().Down && !http.Request.Path.StartsWithSegments("/admin"))
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await http.Response.WriteAsJsonAsync(new { error = $"Cell {cellId} down." });
        return;
    }
    try
    {
        await next(http);
    }
    catch (NotFoundException e)
    {
        http.Response.StatusCode = StatusCodes.Status404NotFound;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
    catch (BusinessRuleException e)
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

// ─── The customers' API (called only through the router) ───
app.MapGet("/customers/{customer}/accounts", (string customer, Store store) => Results.Ok(store.Accounts(customer)));

app.MapGet("/customers/{customer}/accounts/{account:guid}/movements", (string customer, Guid account, Store store) =>
    Results.Ok(store.Movements(customer, account)));

app.MapPost("/customers/{customer}/accounts/{account:guid}/withdrawal", (string customer, Guid account, Withdrawal r, Store store, CellVersion version, TimeProvider clock) =>
{
    if (r.Amount <= 0)
        throw new BusinessRuleException("The amount must be positive.");
    var balance = store.Move(customer, account, s => version.Withdraw(s, r.Amount), "Withdrawal", clock.GetUtcNow());
    return Results.Ok(new { account, balance });
});

app.MapPost("/customers/{customer}/transfers", async (string customer, Transfer v, Store store, IHttpClientFactory factory, TimeProvider clock) =>
{
    if (v.Amount <= 0)
        throw new BusinessRuleException("The amount must be positive.");

    // ① Both customers are here: a local transaction, fast and atomic. It is the case the partitioning must favour.
    if (store.Hosts(v.TargetCustomer))
        return Results.Ok(new { crossCell = false, sourceBalance = store.TransferLocally(customer, v.Source, v.TargetCustomer, v.Target, v.Amount, clock.GetUtcNow()) });

    // ② The beneficiary is in another cell: local debit, then remote credit through the router, idempotent.
    var operationId = Guid.NewGuid();
    var balance = store.Move(customer, v.Source, s => s - v.Amount, $"Transfer to {v.TargetCustomer}", clock.GetUtcNow(), operationId);
    HttpResponseMessage? response = null;
    try
    {
        response = await factory.CreateClient("router").PostAsJsonAsync($"/internal/customers/{v.TargetCustomer}/credits",
            new Credit(operationId, v.Target, v.Amount, $"Transfer from {customer}"));
    }
    catch (HttpRequestException)
    {
        // the router itself is unreachable: handled as an unreachable target cell
    }
    if (response is { IsSuccessStatusCode: true })
        return Results.Ok(new { crossCell = true, sourceBalance = balance, operationId });

    // No transaction between two cells: the debit is compensated.
    store.Move(customer, v.Source, s => s + v.Amount, "Cancellation: transfer not completed", clock.GetUtcNow());
    return response?.StatusCode == System.Net.HttpStatusCode.NotFound
        ? Results.NotFound(new { error = $"Beneficiary not found at {v.TargetCustomer}: transfer cancelled." })
        : Results.Json(new { error = $"The cell of {v.TargetCustomer} is unreachable: transfer cancelled." }, statusCode: StatusCodes.Status502BadGateway);
});

// ─── Internal API: called by the router and the control plane, never by the customers ───
app.MapPost("/internal/customers", (CustomerExport customer, Store store) =>
{
    store.Import(customer);
    return Results.Created($"/customers/{customer.Id}/accounts", null);
});

app.MapGet("/internal/customers/{customer}/export", (string customer, Store store) => Results.Ok(store.Export(customer)));

app.MapDelete("/internal/customers/{customer}", (string customer, Store store) =>
{
    store.Delete(customer);
    return Results.NoContent();
});

app.MapPost("/internal/customers/{customer}/credits", (string customer, Credit c, Store store, TimeProvider clock) =>
{
    var balance = store.Move(customer, c.Account, s => s + c.Amount, c.Label, clock.GetUtcNow(), c.OperationId);
    return Results.Ok(new { account = c.Account, balance });
});

// ─── Operating the cell ───
app.MapGet("/health", (Store store, CellVersion version) =>
{
    var (healthy, detail) = version.Probe();
    return Results.Ok(new { cell = cellId, version = version.Current, healthy, detail, customers = store.CustomerCount, history = version.History });
});

app.MapPut("/admin/version", (VersionChange r, CellVersion version) =>
{
    if (!CellVersion.Known.Contains(r.Version))
        return Results.BadRequest(new { error = $"Unknown version. Known: {string.Join(", ", CellVersion.Known)}." });
    version.Current = r.Version;
    return Results.Ok(new { cell = cellId, version = version.Current });
});

app.MapPut("/admin/outage", (Outage r, CellState state) =>
{
    state.Down = r.Down;
    return Results.Ok(new { cell = cellId, down = state.Down });
});

app.Run();

namespace Cell.App
{
    public sealed record Withdrawal(decimal Amount);
    public sealed record Transfer(Guid Source, string TargetCustomer, Guid Target, decimal Amount);
    public sealed record Credit(Guid OperationId, Guid Account, decimal Amount, string Label);
    public sealed record VersionChange(string Version);
    public sealed record Outage(bool Down);
}
