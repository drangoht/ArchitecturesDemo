using Micro.Accounts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AccountsDb>((sp, options) =>
    options.UseInMemoryDatabase(sp.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Accounts"));
builder.Services.AddHttpClient("broker", (sp, client) =>
    client.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Broker:Url"]!));
builder.Services.AddHostedService<OutboxRelay>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AccountsDb>().SeedDemoData();

app.MapGet("/health", () => Results.Ok(new { service = "accounts", status = "ok" }));
app.MapPublicEndpoints();
app.MapInternalEndpoints();

app.Run();
