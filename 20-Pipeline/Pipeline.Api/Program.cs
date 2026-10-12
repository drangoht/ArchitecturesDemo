using System.Text.Json.Serialization;
using Pipeline.Api;
using Pipeline.Bank;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Import"));
builder.Services.AddSingleton(_ => DemoData.Ledger());
builder.Services.AddSingleton<History>();

var app = builder.Build();

app.MapAccounts();
app.MapImports();
app.MapDemonstrations();

app.Run();

public partial class Program;
