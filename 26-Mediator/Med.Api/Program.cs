using Med.Api;
using Med.Bank;
using Med.Bank.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBank(services => services.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Bank");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BankDb>().SeedDemoData();

BankEndpoints.Map(app);

app.Run();

// Makes Program visible to WebApplicationFactory in the integration tests.
public partial class Program;
