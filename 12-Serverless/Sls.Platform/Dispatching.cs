using Microsoft.Extensions.Hosting;

namespace Sls.Platform;

/// <summary>Reads the queues and starts one invocation per message, in parallel, as long as instances are available.</summary>
internal sealed class QueueDispatcher(ExecutionEngine engine, ManagedServices services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var subscriptions = engine.Definitions.Where(d => d.Queue is not null).ToList();
        while (!stopping.IsCancellationRequested)
        {
            foreach (var definition in subscriptions)
                while (engine.CanStart(definition.Name) && services.Queues.Take(definition.Queue!) is { } message)
                    _ = Handle(definition, message);
            await Task.Delay(20, stopping);
        }
    }

    private async Task Handle(FunctionDefinition definition, QueuedMessage message)
    {
        object trigger = definition.Type == TriggerType.Storage
            ? new StoredObject(message.Body, services.Storage.Read(message.Body) ?? "")
            : new QueueMessage(message.Id, definition.Queue!, message.Body, message.Attempt + 1);
        try
        {
            await engine.Invoke(definition.Name, trigger);
        }
        catch (ThrottlingException)
        {
            services.Queues.PutBack(definition.Queue!, message);
        }
        catch (Exception e)
        {
            services.Log.Write(definition.Name, $"failure (attempt {message.Attempt + 1}): {e.Message}");
            services.Queues.Failed(definition.Queue!, message, e.Message);
        }
    }
}

/// <summary>Fires the scheduled functions (the equivalent of a cron).</summary>
internal sealed class Timers(ExecutionEngine engine, ManagedServices services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var next = engine.Definitions.Where(d => d.Type == TriggerType.Timer)
            .ToDictionary(d => d, d => DateTime.UtcNow + d.Period!.Value);
        while (!stopping.IsCancellationRequested)
        {
            foreach (var (definition, time) in next.ToList())
            {
                if (DateTime.UtcNow < time)
                    continue;
                next[definition] = time + definition.Period!.Value;
                _ = Fire(engine, services, definition.Name);
            }
            await Task.Delay(100, stopping);
        }
    }

    public static async Task Fire(ExecutionEngine engine, ManagedServices services, string name)
    {
        try
        {
            await engine.Invoke(name, new TimerTick(DateTime.UtcNow));
        }
        catch (Exception e)
        {
            services.Log.Write(name, $"failure: {e.Message}");
        }
    }
}

internal sealed class Recycler(ExecutionEngine engine) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            engine.Recycle();
            await Task.Delay(250, stopping);
        }
    }
}
