using System.Text.Json.Serialization;
using Choreo.Api;
using Choreo.Bus;
using Choreo.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var configuration = builder.Configuration.GetSection("Banque").Get<OptionsApi>() ?? new OptionsApi();
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(_ => Banque.Demarrer(new OptionsBanque
{
    DureeReservation = TimeSpan.FromSeconds(configuration.DureeReservationSecondes),
    Fidelite = configuration.Fidelite,
    Bus = new OptionsBus { ProbabiliteDoublon = configuration.ProbabiliteDoublon }
}));
builder.Services.AddHostedService<ExpirationDesReservations>();

var app = builder.Build();

app.MapVirements();
app.MapServices();
app.MapBus();

app.Run();

public partial class Program;
