namespace Res.Resilience;

/// <summary>
/// Retry: a transient failure is tried again, after a wait that doubles each time (exponential backoff),
/// plus a random part (jitter). Only for calls that can be repeated without harm: here, a read.
/// </summary>
public class RetryHandler(RetryOptions options, TimeProvider clock, ResilienceEvents events, Random random) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            string failure;
            try
            {
                var response = await base.SendAsync(request, ct);
                if (!Transient.IsFailure(response) || attempt == options.MaxRetries)
                    return response;
                failure = $"HTTP {(int)response.StatusCode}";
                response.Dispose();
            }
            catch (Exception e) when (Transient.IsRetryable(e) && attempt < options.MaxRetries)
            {
                failure = e.GetType().Name;
            }

            var delay = DelayBefore(attempt + 1);
            events.Add("retry", $"attempt {attempt + 2} in {delay.TotalMilliseconds:0} ms, after {failure}");
            await Task.Delay(delay, clock, ct);
        }
    }

    /// <summary>200 ms, 400 ms, 800 ms… each increased by up to JitterRatio.</summary>
    public TimeSpan DelayBefore(int retry)
    {
        var exponential = options.BaseDelay * Math.Pow(2, retry - 1);
        return exponential * (1 + options.JitterRatio * random.NextDouble());
    }
}
