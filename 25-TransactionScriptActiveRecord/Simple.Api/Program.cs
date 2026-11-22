using Simple.Api;
using Simple.Data;
using Simple.Scripts;

var builder = WebApplication.CreateBuilder(args);

// A single database for both styles: what one writes, the other reads.
builder.Services.AddSingleton(_ => Database.WithDemoData());
builder.Services.AddSingleton<BankScripts>();

var app = builder.Build();

ScriptEndpoints.Map(app.MapGroup("/scripts"));
RecordEndpoints.Map(app.MapGroup("/records"));

app.Run();

// Makes Program visible to WebApplicationFactory in the integration tests.
public partial class Program;
