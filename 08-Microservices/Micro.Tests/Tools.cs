using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Micro.Tests;

public record Call(HttpMethod Method, string Host, string Path, string? Body, string? MessageId);

/// <summary>Fake HTTP server: records the calls received and answers according to a function.</summary>
public class FakeHttp(Func<Call, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _counter;

    public ConcurrentQueue<Call> Calls { get; } = new();

    public static FakeHttp AlwaysOk() => new((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

    public static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = JsonContent.Create(body) };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellation);
        var call = new Call(request.Method, request.RequestUri!.Authority, request.RequestUri.AbsolutePath, body,
            request.Headers.TryGetValues("Message-Id", out var ids) ? ids.First() : null);
        Calls.Enqueue(call);
        return respond(call, Interlocked.Increment(ref _counter));
    }
}

/// <summary>
/// Starts ONE service in memory, with its own database. Its network dependencies (other services, broker)
/// are replaced by fake HTTP servers: that is how a microservice is tested in isolation.
/// </summary>
public class ServiceUnderTest<TMarker>(
    Dictionary<string, FakeHttp>? fakeClients = null,
    Dictionary<string, string>? settings = null) : WebApplicationFactory<TMarker>
    where TMarker : class
{
    private readonly Dictionary<string, FakeHttp> _fakes = fakeClients ?? [];

    /// <summary>The broker is always simulated: a test must never publish to a real broker.</summary>
    public FakeHttp Broker => _fakes.TryGetValue("broker", out var broker) ? broker : _defaultBroker;
    private readonly FakeHttp _defaultBroker = FakeHttp.AlwaysOk();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseName", Guid.NewGuid().ToString());
        foreach (var (key, value) in settings ?? [])
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("broker").ConfigurePrimaryHttpMessageHandler(() => Broker);
            foreach (var (name, fake) in _fakes.Where(f => f.Key != "broker"))
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => fake);
        });
    }
}

public static class Wait
{
    /// <summary>Asynchronous processing (outbox, deliveries) takes a little time: we wait for it to happen.</summary>
    public static async Task Until(Func<bool> condition, int maxDelayMs = 10_000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(maxDelayMs);
        while (!condition() && DateTime.UtcNow < limit)
            await Task.Delay(50);
        Assert.True(condition(), "The expected condition did not happen in time.");
    }
}
