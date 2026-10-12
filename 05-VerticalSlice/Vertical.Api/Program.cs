using Microsoft.EntityFrameworkCore;
using Vertical.Api.Data;
using Vertical.Api.Features.Accounts;
using Vertical.Api.Features.Transfers;

var builder = WebApplication.CreateBuilder(args);

// The database name is read on demand: the integration tests give a different one to each application.
builder.Services.AddDbContext<BankDbContext>((services, options) =>
    options.UseInMemoryDatabase(services.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Bank"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BankDbContext>().SeedDemoData();

// Each slice declares its own route. Adding a feature = one file + one line here.
OpenAccount.Map(app);
GetAccount.Map(app);
WithdrawMoney.Map(app);
MakeTransfer.Map(app);

app.Run();

// Makes Program visible to WebApplicationFactory in the integration tests.
public partial class Program;
