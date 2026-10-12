using Space.Grid;
using Microsoft.Extensions.Options;

namespace Space.Api;

public sealed class ElasticityOptions
{
    /// <summary>Frequency of the load evaluations; 0 = no automatic elasticity (the tests drive it by hand).</summary>
    public int EvaluationPeriodMs { get; set; } = 250;
}

/// <summary>The deployment manager at work: it regularly re-evaluates the load and adjusts the number of units.</summary>
public sealed class AutomaticElasticity(Platform platform, IOptions<ElasticityOptions> options, ILogger<AutomaticElasticity> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (options.Value.EvaluationPeriodMs <= 0)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.EvaluationPeriodMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
                if (await platform.Deployment.Evaluate() is { } decision)
                    logger.LogInformation("Elasticity: {Decision}", decision);
        }
        catch (OperationCanceledException)
        {
            // application shutdown
        }
    }
}
