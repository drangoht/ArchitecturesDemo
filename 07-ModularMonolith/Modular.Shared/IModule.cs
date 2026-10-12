using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Modular.Shared;

/// <summary>
/// What each module implements to plug itself into the host.
/// The host only knows this interface: it knows nothing about what the modules contain.
/// </summary>
public interface IModule
{
    string Name { get; }

    /// <summary>Registers the module's services (its database, its use cases, its event handlers).</summary>
    void Register(IServiceCollection services, IConfiguration configuration);

    /// <summary>Declares the module's HTTP routes: each module owns its own API.</summary>
    void MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>Initialization at startup (demo data…). Optional.</summary>
    Task Initialize(IServiceProvider services) => Task.CompletedTask;
}

public static class ModuleExtensions
{
    /// <summary>
    /// Name of a module's database. Each module has its own (in production: one SQL schema per module).
    /// Read on demand so that the tests can isolate each run.
    /// </summary>
    public static string DatabaseName(this IServiceProvider services, string module) =>
        $"{services.GetRequiredService<IConfiguration>()["DatabaseName"] ?? "Bank"}-{module}";
}
