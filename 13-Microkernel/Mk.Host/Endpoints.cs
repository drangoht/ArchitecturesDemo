using Mk.Kernel;

namespace Mk.Host;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

public sealed record PluginsFolder(string Path);

internal static class Endpoints
{
    internal record WithdrawalRequest(decimal Amount);
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);

    public static void MapBank(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", (Guid id, BankingKernel kernel) =>
            kernel.Get(id) is { } statement ? Results.Ok(statement) : Results.NotFound());

        app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest r, BankingKernel kernel) =>
            ToHttp(kernel.Withdraw(id, r.Amount)));

        app.MapPost("/transfers", (TransferRequest r, BankingKernel kernel) =>
            ToHttp(kernel.Transfer(r.Source, r.Target, r.Amount)));

        // The format is provided by a plugin; without a format plugin, no exportable statement.
        app.MapGet("/accounts/{id:guid}/statement", (Guid id, string format, BankingKernel kernel) =>
            kernel.Export(id, format, out var formats) is { } export
                ? Results.Text(export.Content, $"{export.MimeType}; charset=utf-8")
                : Results.BadRequest(new { error = $"Format \"{format}\" unavailable.", availableFormats = formats }));
    }

    /// <summary>Plugin administration: list, reload, enable, disable — without restarting.</summary>
    public static void MapPluginAdministration(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin/plugins");

        admin.MapGet("", (PluginRegistry registry) => registry.All.Select(p => new
        {
            p.Name,
            p.Instance.Description,
            p.Source,
            State = p.State.ToString(),
            p.Error,
            p.ExtensionPoints
        }));

        admin.MapPost("/reload", (PluginLoader loader, PluginsFolder folder, PluginRegistry registry) =>
        {
            var unloaded = loader.Reload(folder.Path);
            return Results.Ok(new { plugins = registry.All.Select(p => p.Name), unloadedContexts = unloaded.Count });
        });

        admin.MapPost("/{name}/disable", (string name, PluginRegistry registry) =>
            registry.ChangeState(name, active: false) ? Results.NoContent() : Results.NotFound());

        admin.MapPost("/{name}/enable", (string name, PluginRegistry registry) =>
            registry.ChangeState(name, active: true) ? Results.NoContent() : Results.NotFound());
    }

    private static IResult ToHttp(Result result) => result switch
    {
        { Success: true } => Results.Ok(new { balance = result.Balance, fee = result.Fee }),
        { NotFound: true } => Results.NotFound(new { error = result.Error }),
        _ => Results.BadRequest(new { error = result.Error })
    };
}

/// <summary>
/// Watches the plugins folder: dropping, deleting or rebuilding a plugin reloads it automatically.
/// The events are grouped (a build writes several files in a row).
/// </summary>
internal sealed class PluginWatcher(
    PluginLoader loader, PluginsFolder folder, IConfiguration configuration, ILogger<PluginWatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!configuration.GetValue("Plugins:Watch", true) || !Directory.Exists(folder.Path))
            return;

        using var watcher = new FileSystemWatcher(folder.Path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        var lastChange = DateTime.MinValue;
        watcher.Changed += (_, _) => lastChange = DateTime.UtcNow;
        watcher.Created += (_, _) => lastChange = DateTime.UtcNow;
        watcher.Deleted += (_, _) => lastChange = DateTime.UtcNow;
        watcher.Renamed += (_, _) => lastChange = DateTime.UtcNow;
        watcher.EnableRaisingEvents = true;
        logger.LogInformation("Watching {Folder}", folder.Path);

        while (!stopping.IsCancellationRequested)
        {
            await Task.Delay(250, stopping);
            if (lastChange != DateTime.MinValue && DateTime.UtcNow - lastChange > TimeSpan.FromMilliseconds(750))
            {
                lastChange = DateTime.MinValue;
                logger.LogInformation("Change detected in the plugins: reloading.");
                loader.Reload(folder.Path);
            }
        }
    }
}
