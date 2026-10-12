using Micro.Transfers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TransfersDb>((sp, options) =>
    options.UseInMemoryDatabase(sp.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Transfers"));

// Calls to the Accounts service: the network can fail, so we protect ourselves.
// Retry (including on POST: safe, because the calls carry an idempotent operationId),
// timeout per attempt and circuit breaker so as not to insist on a service that is down.
builder.Services.AddHttpClient("accounts", (sp, client) =>
        client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Services:Accounts"]!))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });
builder.Services.AddScoped<AccountsClient>();

builder.Services.AddHttpClient("broker", (sp, client) =>
    client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Broker:Url"]!));
builder.Services.AddHostedService<OutboxRelay>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { service = "transfers", status = "ok" }));
app.MapTransfers();

app.Run();
