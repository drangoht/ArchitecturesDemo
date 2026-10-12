using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Styles.Bank;
using Styles.Client;
using Styles.Tooling.GraphQL;

namespace Styles.Tests;

/// <summary>A stopped clock that is set by hand.</summary>
public sealed class AdjustableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now += duration;
}

/// <summary>
/// The server in memory, and the app's three clients, each with its counter of requests and bytes.
/// All three go through the same test server (gRPC included: it handles HTTP/2 and the trailers).
/// </summary>
public sealed class TestSystem : IAsyncDisposable
{
    // Thursday 1 October 2026, 10:30:15.123 am in Paris.
    public AdjustableClock Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 8, 30, 15, 123, TimeSpan.Zero));

    public WebApplicationFactory<Styles.Server.Marker> Server { get; }

    public HttpCounter RestCounter { get; }
    public HttpCounter GraphQLCounter { get; }
    public HttpCounter GrpcCounter { get; }

    public RestClient Rest { get; }
    public GraphQLClient GraphQL { get; }
    public GrpcBankClient Grpc { get; }

    /// <summary>A raw HTTP client (without counter), to look at the responses closely.</summary>
    public HttpClient Http { get; }

    public TestSystem()
    {
        Server = new WebApplicationFactory<Styles.Server.Marker>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock)));
        Http = Server.CreateClient();
        (RestCounter, Rest) = Create(h => new RestClient(h));
        (GraphQLCounter, GraphQL) = Create(h => new GraphQLClient(h));
        (GrpcCounter, Grpc) = Create(h => new GrpcBankClient(h));
    }

    private (HttpCounter, T) Create<T>(Func<HttpClient, T> factory)
    {
        var counter = new HttpCounter(Server.Server.CreateHandler());
        return (counter, factory(new HttpClient(counter) { BaseAddress = Server.Server.BaseAddress }));
    }

    public IEnumerable<IBankClient> AllThree => [Rest, GraphQL, Grpc];

    public BankService Bank => Server.Services.GetRequiredService<BankService>();

    public GraphQLEngine Engine => Server.Services.GetRequiredService<GraphQLEngine>();

    /// <summary>Executes a GraphQL query directly on the engine (without HTTP).</summary>
    public JsonObject Query(string query, object? variables = null) =>
        Engine.Execute(query, variables is null ? null : System.Text.Json.JsonSerializer.SerializeToNode(variables)!.AsObject());

    public async ValueTask DisposeAsync() => await Server.DisposeAsync();
}
