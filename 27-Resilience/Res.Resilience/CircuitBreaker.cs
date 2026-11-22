namespace Res.Resilience;

public enum CircuitState { Closed, Open, HalfOpen }

/// <summary>
/// The breaker's state, SHARED by every call to the same service (a singleton): it is the whole point.
/// Closed: calls go through, failures are counted. Open: every call fails at once, the service is left alone.
/// Half-open: after the break, ONE trial call decides between closing and opening again.
/// </summary>
public class CircuitBreaker(CircuitBreakerOptions options, TimeProvider clock, ResilienceEvents events)
{
    private readonly Lock _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;

    public CircuitState State { get; private set; } = CircuitState.Closed;

    /// <summary>Called before each attempt: throws if the call must not even be tried.</summary>
    public void BeforeCall()
    {
        lock (_gate)
        {
            if (State == CircuitState.HalfOpen)
                throw new BrokenCircuitException(TimeSpan.Zero);   // the trial call is in progress: no other

            if (State == CircuitState.Open)
            {
                var remaining = _openedAt + options.BreakDuration - clock.GetUtcNow();
                if (remaining > TimeSpan.Zero)
                    throw new BrokenCircuitException(remaining);
                State = CircuitState.HalfOpen;
                events.Add("circuit", "half-open: one trial call");
            }
        }
    }

    public void OnSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            if (State == CircuitState.HalfOpen)
            {
                State = CircuitState.Closed;
                events.Add("circuit", "closed: the trial call succeeded");
            }
        }
    }

    /// <summary>The call ended without a verdict (cancelled by the caller): a trial call frees its place.</summary>
    public void OnAbandoned()
    {
        lock (_gate)
        {
            if (State == CircuitState.HalfOpen)
            {
                State = CircuitState.Open;
                _openedAt = clock.GetUtcNow() - options.BreakDuration;   // the next call may try again at once
            }
        }
    }

    public void OnFailure()
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (State == CircuitState.HalfOpen || _consecutiveFailures >= options.FailureThreshold)
            {
                var reopened = State == CircuitState.HalfOpen;
                State = CircuitState.Open;
                _openedAt = clock.GetUtcNow();
                events.Add("circuit", reopened
                    ? "open again: the trial call failed"
                    : $"open after {_consecutiveFailures} failures in a row, for {options.BreakDuration.TotalSeconds:0} s");
                _consecutiveFailures = 0;   // the next count starts after the break: ONE failed trial is enough to reopen
            }
        }
    }
}

/// <summary>Plugs the shared breaker into the HTTP pipeline: asks before, reports after.</summary>
public class CircuitBreakerHandler(CircuitBreaker breaker) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        breaker.BeforeCall();
        try
        {
            var response = await base.SendAsync(request, ct);
            if (Transient.IsFailure(response))
                breaker.OnFailure();
            else
                breaker.OnSuccess();
            return response;
        }
        catch (Exception e) when (Transient.IsRetryable(e))
        {
            breaker.OnFailure();
            throw;
        }
        catch
        {
            breaker.OnAbandoned();
            throw;
        }
    }
}
