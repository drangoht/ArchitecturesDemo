using Hexa.Adapters.Notifications;
using Hexa.Adapters.Persistence;
using Hexa.Adapters.Web;
using Hexa.Hexagon.Application;
using Hexa.Hexagon.Ports.Inbound;
using Hexa.Hexagon.Ports.Outbound;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// ── Composition root: this is where, and only where, the adapters are chosen ──

// Outbound adapters, swappable by configuration: --Persistence=Json or Memory (default).
builder.Services.AddSingleton<IAccounts>(_ => builder.Configuration["Persistence"] switch
{
    "Json" => new JsonFileAccounts(builder.Configuration["AccountsFile"] ?? "accounts.json", DemoData.Accounts()),
    _ => new InMemoryAccounts(DemoData.Accounts())
});
builder.Services.AddSingleton<INotifications>(new LogNotifications(Console.Out));

// Inbound ports → their implementation in the hexagon.
builder.Services.AddScoped<IGetAccount, GetAccount>();
builder.Services.AddScoped<IWithdrawMoney, WithdrawMoney>();
builder.Services.AddScoped<IMakeTransfer, MakeTransfer>();

var app = builder.Build();

app.MapAccountsEndpoints();

app.Run();
