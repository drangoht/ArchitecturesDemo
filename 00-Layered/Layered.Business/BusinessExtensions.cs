using Layered.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Layered.Business;

/// <summary>
/// The business layer wires the layer below itself:
/// that way the presentation only talks to the layer right below it ("strict" layers).
/// </summary>
public static class BusinessExtensions
{
    public static IServiceCollection AddBusinessLayer(this IServiceCollection services, string databaseName = "Bank")
    {
        services.AddDataLayer(databaseName);
        services.AddScoped<AccountsService>();
        return services;
    }

    public static Task PrepareDemoData(this IServiceProvider services) =>
        services.SeedDemoData();
}
