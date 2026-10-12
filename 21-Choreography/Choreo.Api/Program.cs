using System.Text.Json.Serialization;
using Choreo.Api;
using Choreo.Bus;
using Choreo.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var configuration = builder.Configuration.GetSection("Bank").Get<ApiOptions>() ?? new ApiOptions();
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(_ => Bank.Start(new BankOptions
{
    ReservationDuration = TimeSpan.FromSeconds(configuration.ReservationDurationSeconds),
    Loyalty = configuration.Loyalty,
    Bus = new BusOptions { DuplicateProbability = configuration.DuplicateProbability }
}));
builder.Services.AddHostedService<ReservationExpiry>();

var app = builder.Build();

app.MapTransfers();
app.MapServices();
app.MapBus();

app.Run();

public partial class Program;
