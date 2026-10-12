using Mk.Host;
using Mk.Kernel;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<PluginRegistry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => Log(sp));
builder.Services.AddSingleton(sp => new PluginLoader(sp.GetRequiredService<PluginRegistry>(), Log(sp)));
builder.Services.AddSingleton(sp => new BankingKernel(
    sp.GetRequiredService<PluginRegistry>(), sp.GetRequiredService<TimeProvider>(), Log(sp)));
builder.Services.AddSingleton(sp => new PluginsFolder(Path.GetFullPath(Path.Combine(
    sp.GetRequiredService<IWebHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<IConfiguration>()["Plugins:Folder"] ?? "../plugins"))));
builder.Services.AddHostedService<PluginWatcher>();

var app = builder.Build();

// Initial loading of the plugins, then demo accounts.
app.Services.GetRequiredService<PluginLoader>().Reload(app.Services.GetRequiredService<PluginsFolder>().Path);
var kernel = app.Services.GetRequiredService<BankingKernel>();
kernel.Open(DemoData.Alice, "Alice", 1_000m);
kernel.Open(DemoData.Bob, "Bob", 500m);
kernel.Open(DemoData.Carla, "Carla", 300m);

app.MapBank();
app.MapPluginAdministration();

app.Run();

static Action<string> Log(IServiceProvider sp)
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Kernel");
    return message => logger.LogInformation("{Message}", message);
}

public partial class Program;
