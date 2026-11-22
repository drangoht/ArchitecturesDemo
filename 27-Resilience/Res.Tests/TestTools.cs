using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Res.FraudSimulator;
using Res.Resilience;

namespace Res.Tests;

public static class ClockDriving
{
    /// <summary>
    /// Moves the fake clock forward in small steps until the task ends: waits, timeouts and the partner's latency
    /// all follow this clock, so a 30-second break lasts a few milliseconds of real time.
    /// </summary>
    public static async Task<T> Drive<T>(this FakeTimeProvider clock, Task<T> task)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > giveUp)
                throw new TimeoutException("The task never ended, even with the clock moving.");
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(1);   // real time: lets the continuations run
        }
        return await task;
    }

    public static Task Drive(this FakeTimeProvider clock, Task task) =>
        clock.Drive(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));
}

/// <summary>The last handler of a test pipeline: answers what the test says, and counts the calls.</summary>
public class ScriptedService(Func<int, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    public static ScriptedService Always(HttpStatusCode status) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    /// <summary>The given answers in order, then 200 for ever.</summary>
    public static ScriptedService Sequence(params HttpStatusCode[] statuses) =>
        new((call, _) => Task.FromResult(new HttpResponseMessage(call <= statuses.Length ? statuses[call - 1] : HttpStatusCode.OK)));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        answer(Interlocked.Increment(ref _calls), ct);
}

public static class Pipeline
{
    /// <summary>An HttpClient whose handlers are given from the outside in, the service last.</summary>
    public static HttpClient Build(HttpMessageHandler service, params DelegatingHandler[] handlers)
    {
        HttpMessageHandler inner = service;
        foreach (var handler in handlers.Reverse())
        {
            handler.InnerHandler = inner;
            inner = handler;
        }
        return new HttpClient(inner) { BaseAddress = new Uri("http://service.example/") };
    }
}

/// <summary>The complete API, in memory, on a fake clock, without jitter (the waits are then exact).</summary>
public class BankApplication : WebApplicationFactory<Program>
{
    public FakeTimeProvider Clock { get; } = new();

    public FraudPartner Partner => Services.GetRequiredService<FraudPartner>();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(new ResilienceOptions { Retry = new RetryOptions { JitterRatio = 0 } });
        });
}

public record OutcomeJson(string Status, decimal SourceBalance, decimal TargetBalance);

public record AccountJson(Guid Id, string Owner, decimal Balance, List<string> Operations);

public record ResilienceJson(string Circuit, int InProgress, int Queued, string PartnerMode, int PartnerCallsReceived, List<string> Events);
