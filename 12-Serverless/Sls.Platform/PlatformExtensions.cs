using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sls.Platform;

public record SettingValue(string Value);

public static class PlatformExtensions
{
    /// <summary>"Deploys" the functions described by the manifest onto the platform.</summary>
    public static IServiceCollection AddServerlessPlatform(this IServiceCollection services, Action<Manifest> deployment)
    {
        var manifest = new Manifest();
        deployment(manifest);

        services.AddSingleton(manifest);
        services.AddSingleton(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection("Platform").Get<PlatformOptions>() ?? new PlatformOptions());
        services.AddSingleton(sp => new ManagedServices(
            sp.GetRequiredService<PlatformOptions>(),
            sp.GetRequiredService<IConfiguration>().GetSection("Settings").AsEnumerable(makePathsRelative: true),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("Platform")));
        services.AddSingleton<ExecutionEngine>();
        services.AddHostedService<QueueDispatcher>();
        services.AddHostedService<Timers>();
        services.AddHostedService<Recycler>();
        return services;
    }

    public static void MapServerlessPlatform(this WebApplication app)
    {
        var engine = app.Services.GetRequiredService<ExecutionEngine>();
        var services = app.Services.GetRequiredService<ManagedServices>();

        // HTTP gateway: each route of the manifest triggers its function.
        foreach (var definition in engine.Definitions.Where(d => d.Type == TriggerType.Http))
            app.MapMethods(definition.Route!, [definition.Method!],
                (Func<HttpContext, Task<IResult>>)(http => InvokeHttp(http, definition.Name, engine)));

        // Cloud provider "console": observe, configure, repair.
        var console = app.MapGroup("/platform");
        console.MapGet("/functions", () => engine.Statistics());
        console.MapGet("/bill", () => engine.Bill());
        console.MapGet("/log", (int? count) => services.Log.Latest(count ?? 50));
        console.MapGet("/queues", () => services.Queues.State());
        console.MapPost("/queues/{queue}/replay", (string queue) => Results.Ok(new { replayed = services.Queues.Replay(queue) }));
        console.MapGet("/settings", () => services.Settings);
        console.MapPut("/settings/{key}", (string key, SettingValue setting) =>
        {
            services.Settings[key] = setting.Value;
            return Results.NoContent();
        });
        console.MapGet("/storage", (string? prefix) => services.Storage.List(prefix ?? ""));
        console.MapGet("/storage/content", (string path) =>
            services.Storage.Read(path) is { } content ? Results.Text(content) : Results.NotFound());
        console.MapPost("/timers/{function}/fire", async (string function) =>
        {
            await Timers.Fire(engine, services, function);
            return Results.Accepted();
        });
    }

    private static async Task<IResult> InvokeHttp(HttpContext http, string name, ExecutionEngine engine)
    {
        using var reader = new StreamReader(http.Request.Body);
        var request = new HttpRequestData(http.Request.Method, http.Request.Path,
            http.Request.RouteValues.ToDictionary(p => p.Key, p => p.Value?.ToString() ?? ""),
            await reader.ReadToEndAsync());
        try
        {
            var result = await engine.Invoke(name, request);
            var response = (HttpResponseData)result.Value!;

            // Demo headers: which instance answered, was it "cold", how much time was billed.
            http.Response.Headers["X-Instance"] = $"{name}#{result.Instance}";
            http.Response.Headers["X-Cold-Start"] = result.ColdStart ? "yes" : "no";
            http.Response.Headers["X-Duration-Ms"] = result.DurationMs.ToString();
            if (response.Location is not null)
                http.Response.Headers.Location = response.Location;

            return response.Body is null ? Results.StatusCode(response.Status) : Results.Json(response.Body, statusCode: response.Status);
        }
        catch (ThrottlingException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status429TooManyRequests);
        }
        catch (TimeoutExceededException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (Exception e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
