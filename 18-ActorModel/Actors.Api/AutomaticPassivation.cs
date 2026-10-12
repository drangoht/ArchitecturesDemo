using Actors.Runtime;
using Microsoft.Extensions.Options;

namespace Actors.Api;

public sealed class ActorOptions
{
    /// <summary>An actor without a message for this long is passivated (its memory released, its durable state kept).</summary>
    public int InactivitySeconds { get; set; } = 30;
    public int PassivationPeriodSeconds { get; set; } = 5;
    public int MaxRestarts { get; set; } = 3;
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Beyond it, a request to an actor answers 504 (the message, however, stays in the mailbox).</summary>
    public int AskTimeoutMs { get; set; } = 5_000;
}

/// <summary>The actors' "garbage collector": regularly comes by to put to sleep those no longer in use.</summary>
public sealed class AutomaticPassivation(ActorSystem system, IOptions<ActorOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try
        {
            await system.PassivatePeriodically(TimeSpan.FromSeconds(options.Value.InactivitySeconds),
                TimeSpan.FromSeconds(options.Value.PassivationPeriodSeconds), stopping);
        }
        catch (OperationCanceledException)
        {
            // application shutdown
        }
    }
}
