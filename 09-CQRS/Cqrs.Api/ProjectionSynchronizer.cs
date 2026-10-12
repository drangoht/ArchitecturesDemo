using Cqrs.Events;
using Cqrs.Read.Projections;

namespace Cqrs.Api;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

/// <summary>
/// Reads the new events of the write side and applies them to the read side.
/// Runs in the background: the read side "lags" a moment behind the write side (eventual consistency).
/// The Projections:DelayMs setting deliberately slows the projection down to make this lag visible.
/// </summary>
internal sealed class ProjectionSynchronizer(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<ProjectionSynchronizer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var delay = TimeSpan.FromMilliseconds(configuration.GetValue("Projections:DelayMs", 0));

        while (!stopping.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var stream = scope.ServiceProvider.GetRequiredService<IEventStream>();
            var projector = scope.ServiceProvider.GetRequiredService<Projector>();

            var newEvents = await stream.ReadAfter(await projector.LastSequence(), maximum: 100);
            if (newEvents.Count == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), stopping);
                continue;
            }

            await Task.Delay(delay, stopping);
            foreach (var @event in newEvents)
            {
                await projector.Apply(@event);
                logger.LogInformation("Projected #{Sequence} {Type}", @event.Sequence, @event.Event.GetType().Name);
            }
        }
    }
}
