using System.Text;
using System.Text.Json.Nodes;

namespace Fig.Facade;

/// <summary>A response read entirely, to be able to send it back to the client AND compare it.</summary>
public sealed record ReadResponse(int Status, string? ContentType, byte[] Body, List<KeyValuePair<string, string[]>> Headers)
{
    public string Text => Encoding.UTF8.GetString(Body);
}

/// <summary>
/// The relay: finds the route, sends it to the system in charge of it and copies its response. No business logic:
/// the facade knows nothing about accounts or transfers, only who answers what.
/// </summary>
public sealed class Relay(IHttpClientFactory factory, RoutingTable table, ShadowComparer comparer, FacadeOptions options)
{
    private static readonly HashSet<string> NotRelayed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "Content-Length"
    };

    public async Task Handle(HttpContext http)
    {
        var path = http.Request.Path.Value ?? "/";
        // The routes added for the migration (log, import, fees) only concern the two systems.
        if (path.StartsWith("/internal", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var route = table.Find(http.Request.Method, path);
        var mode = route?.Mode ?? Mode.Legacy;   // what nobody took over stays with the legacy system: that is the starting point
        table.Count(route?.Name ?? RoutingTable.Undeclared, byLegacy: mode != Mode.Modern);
        var body = await ReadBody(http.Request);
        var system = mode == Mode.Modern ? "modern" : "legacy";

        ReadResponse response;
        if (mode == Mode.Shadow)
        {
            // Both calls leave at the same time; the client only waits for the legacy system.
            var budget = new CancellationTokenSource(options.ShadowBudgetMs);
            var shadow = Send("modern", Message(http.Request, body), budget.Token);
            try
            {
                response = await Send("legacy", Message(http.Request, body), http.RequestAborted);
            }
            catch (Exception e)
            {
                _ = comparer.Abandon(shadow, budget);
                if (e is not HttpRequestException)
                    throw;
                await Unreachable(http, system);
                return;
            }
            // Compared AFTERWARDS, without being awaited: a slow or failed modern service slows nobody down.
            _ = comparer.Compare(route!.Name, $"{http.Request.Method} {path}{http.Request.QueryString}", response, shadow, budget);
        }
        else
        {
            try
            {
                response = await Send(system, Message(http.Request, body), http.RequestAborted);
            }
            catch (HttpRequestException)
            {
                await Unreachable(http, system);
                return;
            }
        }

        http.Response.StatusCode = response.Status;
        foreach (var (name, values) in response.Headers.Where(e => !NotRelayed.Contains(e.Key)))
            http.Response.Headers[name] = values;
        http.Response.Headers["X-Served-By"] = system;
        await http.Response.Body.WriteAsync(response.Body, http.RequestAborted);
    }

    // The message is built right away, while the client's request still exists: the shadow call outlives it.
    private static HttpRequestMessage Message(HttpRequest request, byte[]? body)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), $"{request.Path}{request.QueryString}");
        if (body is not null)
            message.Content = new ByteArrayContent(body);
        foreach (var (name, values) in request.Headers.Where(e => !NotRelayed.Contains(e.Key)))
            if (!message.Headers.TryAddWithoutValidation(name, values.ToArray()))
                message.Content?.Headers.TryAddWithoutValidation(name, values.ToArray());
        return message;
    }

    private async Task<ReadResponse> Send(string system, HttpRequestMessage message, CancellationToken cancellation)
    {
        using (message)
        using (var response = await factory.CreateClient(system).SendAsync(message, cancellation))
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellation);
            var headers = response.Headers.Concat(response.Content.Headers).Select(e => KeyValuePair.Create(e.Key, e.Value.ToArray())).ToList();
            return new ReadResponse((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType, body, headers);
        }
    }

    // The body is read entirely (it may have to leave twice), whether it has an announced length or arrives in chunks.
    private static async Task<byte[]?> ReadBody(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    private static Task Unreachable(HttpContext http, string system)
    {
        http.Response.StatusCode = StatusCodes.Status502BadGateway;
        http.Response.Headers["X-Served-By"] = system;
        return http.Response.WriteAsJsonAsync(new { error = $"System {system} unreachable." });
    }
}

public sealed record ShadowComparison(DateTimeOffset Date, string Route, string Request, string Result, string? Reason,
    int LegacyStatus, int? ModernStatus, string? LegacyBody, string? ModernBody);

/// <summary>
/// The shadow mode: the legacy system's response is authoritative, the modern one's is compared silently. This builds up the
/// proof, on real traffic, that the modern service answers the same, BEFORE entrusting it with the route.
/// </summary>
public sealed class ShadowComparer(ILogger<ShadowComparer> logger)
{
    private const int Kept = 20;
    private readonly object _lock = new();
    private readonly LinkedList<ShadowComparison> _lastDivergences = new();
    private long _identical, _divergences, _abandoned;

    public async Task Compare(string route, string request, ReadResponse legacy, Task<ReadResponse> shadow, CancellationTokenSource budget)
    {
        using (budget)
        {
            ReadResponse modern;
            try
            {
                modern = await shadow;
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
            {
                // Too slow or down: it is not a divergence, but it is noted (the modern service must also bear the load).
                Interlocked.Increment(ref _abandoned);
                logger.LogInformation("Shadow abandoned for {Request}: {Reason}", request, e is OperationCanceledException ? "budget exceeded" : e.Message);
                return;
            }

            var reason = Difference(legacy, modern);
            if (reason is null)
            {
                Interlocked.Increment(ref _identical);
                return;
            }
            Interlocked.Increment(ref _divergences);
            logger.LogWarning("DIVERGENCE {Route} ({Request}): {Reason}", route, request, reason);
            lock (_lock)
            {
                _lastDivergences.AddFirst(new ShadowComparison(DateTimeOffset.UtcNow, route, request, "divergence", reason,
                    legacy.Status, modern.Status, Truncate(legacy.Text), Truncate(modern.Text)));
                if (_lastDivergences.Count > Kept)
                    _lastDivergences.RemoveLast();
            }
        }
    }

    public async Task Abandon(Task<ReadResponse> shadow, CancellationTokenSource budget)
    {
        using (budget)
        {
            try { await shadow; }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
        }
    }

    public object Report()
    {
        lock (_lock)
        {
            var identical = Interlocked.Read(ref _identical);
            var divergences = Interlocked.Read(ref _divergences);
            return new
            {
                compared = identical + divergences,
                identical,
                divergences,
                abandoned = Interlocked.Read(ref _abandoned),
                lastDivergences = _lastDivergences.ToList()
            };
        }
    }

    /// <summary>Same status and same content. The JSON is compared structurally (spaces, property order: irrelevant).</summary>
    public static string? Difference(ReadResponse legacy, ReadResponse modern)
    {
        if (legacy.Status != modern.Status)
            return $"status {legacy.Status} ≠ {modern.Status}";
        if (legacy.ContentType != modern.ContentType)
            return $"content type {legacy.ContentType} ≠ {modern.ContentType}";
        if (legacy.ContentType?.Contains("json") == true && legacy.Body.Length > 0 && modern.Body.Length > 0)
            return JsonNode.DeepEquals(JsonNode.Parse(legacy.Body), JsonNode.Parse(modern.Body)) ? null : "different JSON content";
        return legacy.Body.AsSpan().SequenceEqual(modern.Body) ? null : "different content";
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500] + "…";
}
