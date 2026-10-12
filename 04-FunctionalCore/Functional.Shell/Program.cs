using Functional.Shell;
using Functional.Shell.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BankDbContext>(options => options.UseInMemoryDatabase("Bank"));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<BankDbContext>().SeedDemoData();

app.MapBank();

app.Run();
