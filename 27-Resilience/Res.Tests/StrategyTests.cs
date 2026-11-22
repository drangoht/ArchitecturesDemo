using System.Net;
using Microsoft.Extensions.Time.Testing;
using Res.Resilience;

namespace Res.Tests;

/// <summary>Each strategy alone (or two together), in front of a scripted service, on a fake clock.</summary>
public class StrategyTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly ResilienceEvents _events;
    private static readonly RetryOptions NoJitter = new() { JitterRatio = 0 };

    /// <summary>A real-time guard: a test that waits for a call which never ends fails instead of hanging.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    public StrategyTests() => _events = new ResilienceEvents(_clock);

    private RetryHandler Retry(RetryOptions? options = null) => new(options ?? NoJitter, _clock, _events, new Random(42));

    private CircuitBreaker Breaker(int threshold = 5) =>
        new(new CircuitBreakerOptions { FailureThreshold = threshold, BreakDuration = TimeSpan.FromSeconds(30) }, _clock, _events);

    // ---------- Retry ----------

    [Fact]
    public async Task Transient_failures_are_retried_until_the_service_answers()
    {
        var service = ScriptedService.Sequence(HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError);
        var client = Pipeline.Build(service, Retry());

        var response = await _clock.Drive(client.GetAsync("check"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, service.Calls);
    }

    [Fact]
    public async Task After_the_last_retry_the_last_answer_is_returned()
    {
        var service = ScriptedService.Always(HttpStatusCode.ServiceUnavailable);
        var client = Pipeline.Build(service, Retry());

        var response = await _clock.Drive(client.GetAsync("check"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(4, service.Calls);   // 1 call + 3 retries
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_client_error_is_never_retried(HttpStatusCode status)
    {
        var service = ScriptedService.Always(status);
        var client = Pipeline.Build(service, Retry());

        await _clock.Drive(client.GetAsync("check"));

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task The_wait_doubles_before_each_retry()
    {
        var client = Pipeline.Build(ScriptedService.Always(HttpStatusCode.ServiceUnavailable), Retry());

        await _clock.Drive(client.GetAsync("check"));

        Assert.Equal(
            ["attempt 2 in 200 ms, after HTTP 503", "attempt 3 in 400 ms, after HTTP 503", "attempt 4 in 800 ms, after HTTP 503"],
            _events.Latest(10).Select(e => e.Detail));
    }

    [Fact]
    public async Task The_retry_really_waits_on_the_clock_between_attempts()
    {
        var callTimes = new List<DateTimeOffset>();
        var service = new ScriptedService((_, _) =>
        {
            callTimes.Add(_clock.GetUtcNow());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });
        var client = Pipeline.Build(service, Retry());

        await _clock.Drive(client.GetAsync("check"));

        var gaps = callTimes.Zip(callTimes.Skip(1), (before, after) => (after - before).TotalMilliseconds).ToList();
        Assert.Equal(3, gaps.Count);
        Assert.True(gaps[0] >= 200 && gaps[1] >= 400 && gaps[2] >= 800, string.Join(", ", gaps));
    }

    [Fact]
    public void The_jitter_adds_up_to_20_percent_and_differs_from_one_retry_to_the_next()
    {
        var retry = Retry(new RetryOptions { JitterRatio = 0.2 });

        var delays = Enumerable.Range(0, 50).Select(_ => retry.DelayBefore(1).TotalMilliseconds).ToList();

        Assert.All(delays, d => Assert.InRange(d, 200, 240));
        Assert.True(delays.Distinct().Count() > 40);
    }

    // ---------- Timeout ----------

    [Fact]
    public async Task A_slow_answer_becomes_a_TimeoutRejectedException()
    {
        var service = new ScriptedService(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _clock, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = Pipeline.Build(service, new TimeoutHandler(TimeSpan.FromSeconds(1), _clock, _events));

        await Assert.ThrowsAsync<TimeoutRejectedException>(() => _clock.Drive(client.GetAsync("check")));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_reported_as_a_timeout()
    {
        var service = new ScriptedService(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), _clock, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = Pipeline.Build(service, new TimeoutHandler(TimeSpan.FromSeconds(1), _clock, _events));
        using var caller = new CancellationTokenSource();

        var call = client.GetAsync("check", caller.Token);
        await caller.CancelAsync();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.IsNotType<TimeoutRejectedException>(error);
        Assert.Equal(0, _events.Count("timeout"));
    }

    [Fact]
    public async Task When_the_caller_has_given_up_too_its_cancellation_wins_over_the_timeout()
    {
        var gate = new TaskCompletionSource();
        using var caller = new CancellationTokenSource();
        var service = new ScriptedService(async (_, _) =>
        {
            await gate.Task;   // a service that notices the cancellation late
            throw new OperationCanceledException(caller.Token);
        });
        var client = Pipeline.Build(service, new TimeoutHandler(TimeSpan.FromSeconds(1), _clock, _events));

        var call = client.GetAsync("check", caller.Token);
        _clock.Advance(TimeSpan.FromSeconds(2));   // our delay has run out…
        await caller.CancelAsync();                // …and the caller has left as well
        gate.SetResult();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Guard));
        Assert.IsNotType<TimeoutRejectedException>(error);
    }

    [Fact]
    public async Task A_timeout_is_retried_like_a_server_error()
    {
        var service = new ScriptedService(async (call, ct) =>
        {
            if (call == 1)
                await Task.Delay(TimeSpan.FromSeconds(5), _clock, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = Pipeline.Build(service, Retry(), new TimeoutHandler(TimeSpan.FromSeconds(1), _clock, _events));

        var response = await _clock.Drive(client.GetAsync("check"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, service.Calls);
    }

    // ---------- Circuit breaker ----------

    [Fact]
    public async Task Five_failures_in_a_row_open_the_circuit_and_the_service_is_left_alone()
    {
        var service = ScriptedService.Always(HttpStatusCode.ServiceUnavailable);
        var breaker = Breaker();
        var client = Pipeline.Build(service, new CircuitBreakerHandler(breaker));

        for (var i = 0; i < 5; i++)
            await client.GetAsync("check");
        var refused = await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("check"));

        Assert.Equal((CircuitState.Open, 5), (breaker.State, service.Calls));
        Assert.Equal(TimeSpan.FromSeconds(30), refused.RetryAfter);
    }

    [Fact]
    public async Task A_success_resets_the_count_of_failures()
    {
        var down = HttpStatusCode.ServiceUnavailable;
        var service = ScriptedService.Sequence(down, down, down, down, HttpStatusCode.OK, down, down, down, down);
        var breaker = Breaker();
        var client = Pipeline.Build(service, new CircuitBreakerHandler(breaker));

        for (var i = 0; i < 9; i++)
            await client.GetAsync("check");

        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    [Fact]
    public async Task After_the_break_a_successful_trial_call_closes_the_circuit()
    {
        var failing = true;
        var service = new ScriptedService((_, _) =>
            Task.FromResult(new HttpResponseMessage(failing ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        var breaker = Breaker(threshold: 2);
        var client = Pipeline.Build(service, new CircuitBreakerHandler(breaker));
        await client.GetAsync("check");
        await client.GetAsync("check");

        _clock.Advance(TimeSpan.FromSeconds(29));
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("check"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        failing = false;
        var trial = await client.GetAsync("check");

        Assert.Equal((HttpStatusCode.OK, CircuitState.Closed), (trial.StatusCode, breaker.State));
    }

    [Fact]
    public async Task A_failed_trial_call_opens_the_circuit_again_at_once()
    {
        var service = ScriptedService.Always(HttpStatusCode.ServiceUnavailable);
        var breaker = Breaker(threshold: 2);
        var client = Pipeline.Build(service, new CircuitBreakerHandler(breaker));
        await client.GetAsync("check");
        await client.GetAsync("check");

        _clock.Advance(TimeSpan.FromSeconds(30));
        await client.GetAsync("check");   // the trial: one failure is enough

        Assert.Equal(CircuitState.Open, breaker.State);
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("check"));
        Assert.Equal(3, service.Calls);
    }

    [Fact]
    public async Task During_the_trial_call_every_other_call_is_refused()
    {
        var release = new TaskCompletionSource();
        var service = new ScriptedService(async (call, _) =>
        {
            if (call <= 2)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var breaker = Breaker(threshold: 2);
        var client = Pipeline.Build(service, new CircuitBreakerHandler(breaker));
        await client.GetAsync("check");
        await client.GetAsync("check");
        _clock.Advance(TimeSpan.FromSeconds(30));

        var trial = client.GetAsync("check");
        await Assert.ThrowsAsync<BrokenCircuitException>(() => client.GetAsync("check").WaitAsync(Guard));
        release.SetResult();

        Assert.Equal(HttpStatusCode.OK, (await trial).StatusCode);
        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    [Fact]
    public async Task A_trial_call_cancelled_by_its_caller_lets_the_next_call_try()
    {
        var service = new ScriptedService(async (call, ct) =>
        {
            if (call <= 2)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (call == 3)
                await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var breaker = Breaker(threshold: 2);
        var client = Pipeline.Build(service, new CircuitBreakerHandler(breaker));
        await client.GetAsync("check");
        await client.GetAsync("check");
        _clock.Advance(TimeSpan.FromSeconds(30));
        using var caller = new CancellationTokenSource();

        var trial = client.GetAsync("check", caller.Token);
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trial.WaitAsync(Guard));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("check").WaitAsync(Guard)).StatusCode);
        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    // ---------- Bulkhead ----------

    [Fact]
    public async Task The_bulkhead_lets_2_calls_through_queues_2_and_refuses_the_next_at_once()
    {
        var release = new TaskCompletionSource();
        var inProgress = 0;
        var maxInProgress = 0;
        var service = new ScriptedService(async (_, _) =>
        {
            var now = Interlocked.Increment(ref inProgress);
            InterlockedMax(ref maxInProgress, now);
            await release.Task;
            Interlocked.Decrement(ref inProgress);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var bulkhead = new Bulkhead(new BulkheadOptions { MaxConcurrency = 2, MaxQueue = 2 }, _events);
        var client = Pipeline.Build(service, new BulkheadHandler(bulkhead));

        var calls = Enumerable.Range(0, 4).Select(_ => client.GetAsync("check")).ToList();
        await WaitUntil(() => bulkhead.InProgress == 2 && bulkhead.Queued == 2);
        await Assert.ThrowsAsync<BulkheadRejectedException>(() => client.GetAsync("check").WaitAsync(Guard));
        release.SetResult();
        await Task.WhenAll(calls).WaitAsync(Guard);

        Assert.Equal((4, 2), (service.Calls, maxInProgress));
        Assert.Equal((0, 0), (bulkhead.InProgress, bulkhead.Queued));
    }

    // ---------- Together ----------

    [Fact]
    public async Task The_retry_never_retries_a_broken_circuit()
    {
        var service = ScriptedService.Always(HttpStatusCode.ServiceUnavailable);
        var breaker = Breaker(threshold: 2);
        var client = Pipeline.Build(service, Retry(), new CircuitBreakerHandler(breaker));

        await Assert.ThrowsAsync<BrokenCircuitException>(() => _clock.Drive(client.GetAsync("check")));

        Assert.Equal(2, service.Calls);   // the third attempt is refused by the breaker…
        Assert.Equal(2, _events.Count("retry"));   // …and not retried: only the two 503 were
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > giveUp)
                throw new TimeoutException("The condition never became true.");
            await Task.Delay(5);
        }
    }
}
