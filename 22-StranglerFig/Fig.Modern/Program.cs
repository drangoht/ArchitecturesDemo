using Fig.Modern;
using Microsoft.Extensions.DependencyInjection.Extensions;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();
builder.Services.AddSingleton<Synchronization>();
builder.Services.AddHostedService<PeriodicSynchronization>();
builder.Services.AddHttpClient<LegacyClient>(c => c.BaseAddress = new Uri(builder.Configuration["Legacy:Url"]!));

var app = builder.Build();

app.Use(async (http, next) =>
{
    try
    {
        await next(http);
    }
    catch (BusinessError e)
    {
        http.Response.StatusCode = e.Status;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

// ─── The historical contract, reproduced route by route ───

app.MapGet("/accounts/{number}", (string number, Store store) =>
    Results.Ok(store.Read(number, Contract.Account)));

app.MapGet("/accounts/{number}/operations", (string number, Store store) =>
    Results.Ok(store.Read(number, Contract.Operations)));

app.MapGet("/accounts/{number}/statement", (string number, string? month, Store store, TimeProvider clock) =>
{
    var period = Contract.Period(month, clock.GetUtcNow());
    return Results.Text(store.Read(number, a => Contract.Statement(a, period)), "text/plain; charset=utf-8");
});

app.MapPost("/transfers", (TransferRequest r, Store store, TimeProvider clock) =>
{
    var now = clock.GetUtcNow();
    now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));   // the second: precision of the contract
    return Results.Ok(Contract.Transfer(store.Transfer(r.Source, r.Target, r.Amount, now)));
});

// ─── For operations (not relayed by the facade) ───

app.MapPost("/internal/synchronize", async (Synchronization synchronization, CancellationToken cancellation) =>
    Results.Ok(await synchronization.Run(cancellation)));

app.MapGet("/internal/state", (Store store) => Results.Ok(new
{
    legacyPosition = store.LegacyPosition,
    toForward = store.ToForward().Count,
    accounts = store.Numbers()
}));

app.Run();
