namespace Res.Resilience;

/// <summary>
/// Timeout, per attempt: past the delay, the call is cancelled and becomes a TimeoutRejectedException,
/// a failure like any other for the breaker and the retry around it.
/// </summary>
public class TimeoutHandler(TimeSpan timeout, TimeProvider clock, ResilienceEvents events) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);
        try
        {
            return await base.SendAsync(request, linked.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our delay ran out, not the caller's patience: the caller's own cancellation goes up unchanged.
            events.Add("timeout", $"no answer within {timeout.TotalMilliseconds:0} ms");
            throw new TimeoutRejectedException(timeout);
        }
    }
}
