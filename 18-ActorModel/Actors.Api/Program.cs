using System.Text.Json.Serialization;
using Actors.Api;
using Actors.Bank;
using Actors.Runtime;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ActorOptions>(builder.Configuration.GetSection("Actors"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<Traps>();
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ActorOptions>>().Value;
    var clock = sp.GetRequiredService<TimeProvider>();
    return new ActorSystem(clock) { AskTimeout = TimeSpan.FromMilliseconds(options.AskTimeoutMs) }.InstallBank(sp.GetRequiredService<StateStore>(), sp.GetRequiredService<Traps>(),
        clock, new SupervisionStrategy(options.MaxRestarts, TimeSpan.FromSeconds(options.WindowSeconds)));
});
builder.Services.AddHostedService<AutomaticPassivation>();

var app = builder.Build();

// The demo accounts are opened like the others: by a message to their actor.
var system = app.Services.GetRequiredService<ActorSystem>();
await system.Account(DemoData.Alice).Ask(new OpenAccount("Alice", 1_000m));
await system.Account(DemoData.Bob).Ask(new OpenAccount("Bob", 500m));

// What the runtime reports → HTTP, once for all the routes.
app.Use(async (http, next) =>
{
    try
    {
        await next(http);
    }
    catch (ActorFailedException e)
    {
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await http.Response.WriteAsJsonAsync(new { error = e.Message, actor = e.Path, next = "the actor was restarted; the request can be replayed" });
    }
    catch (ActorUnavailableException e)
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await http.Response.WriteAsJsonAsync(new { error = e.Message, actor = e.Path });
    }
    catch (TimeoutException e)
    {
        http.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        await http.Response.WriteAsJsonAsync(new { error = e.Message });
    }
});

app.MapBank();
app.MapSystem();
app.MapDemonstrations();

app.Run();

public partial class Program;
