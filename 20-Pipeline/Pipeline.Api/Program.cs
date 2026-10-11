using System.Text.Json.Serialization;
using Pipeline.Api;
using Pipeline.Banque;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<OptionsApi>(builder.Configuration.GetSection("Import"));
builder.Services.AddSingleton(_ => DonneesDeDemo.Registre());
builder.Services.AddSingleton<Historique>();

var app = builder.Build();

app.MapComptes();
app.MapImports();
app.MapDemonstrations();

app.Run();

public partial class Program;
