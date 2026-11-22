namespace Res.Resilience;

/// <summary>The settings of the four strategies. The defaults are those of the demo's API.</summary>
public record ResilienceOptions
{
    public RetryOptions Retry { get; init; } = new();
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public CircuitBreakerOptions CircuitBreaker { get; init; } = new();
    public BulkheadOptions Bulkhead { get; init; } = new();
}

public record RetryOptions
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Up to +20 % of random extra wait, so that clients that failed together don't retry together.</summary>
    public double JitterRatio { get; init; } = 0.2;
}

public record CircuitBreakerOptions
{
    public int FailureThreshold { get; init; } = 5;
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(30);
}

public record BulkheadOptions
{
    public int MaxConcurrency { get; init; } = 2;
    public int MaxQueue { get; init; } = 2;
}

// What the strategies throw when they refuse a call: the caller can tell them apart and fall back.

public class TimeoutRejectedException(TimeSpan timeout)
    : Exception($"No answer within {timeout.TotalMilliseconds:0} ms.");

public class BrokenCircuitException(TimeSpan retryAfter)
    : Exception($"The circuit is open: no call for {Math.Ceiling(retryAfter.TotalSeconds):0} s.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public class BulkheadRejectedException()
    : Exception("Too many calls in progress: the bulkhead is full.");

public static class Transient
{
    /// <summary>A failure that may go away by itself: server error, timeout, overload.</summary>
    public static bool IsFailure(HttpResponseMessage response) =>
        (int)response.StatusCode >= 500 || (int)response.StatusCode is 408 or 429;

    /// <summary>Worth another try. NOT a broken circuit nor a full bulkhead: retrying them would defeat their purpose.</summary>
    public static bool IsRetryable(Exception e) => e is HttpRequestException or TimeoutRejectedException;
}
