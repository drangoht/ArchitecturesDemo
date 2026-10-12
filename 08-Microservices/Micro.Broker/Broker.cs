using System.Text;

namespace Micro.Broker;

/// <summary>Marker type of the service (used by the tests to start THIS service).</summary>
public sealed class Marker;

internal class Delivery(string messageId, string topic, string url, string body)
{
    public string MessageId { get; } = messageId;
    public string Topic { get; } = topic;
    public string Url { get; } = url;
    public string Body { get; } = body;
    public int Attempts { get; set; }
    public DateTime NextAttempt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
}

/// <summary>
/// In-memory delivery queue. The subscriptions are declared in appsettings.json ("Subscriptions" section).
/// Guarantee: "at least once". A message can be delivered twice, hence the idempotence on the subscribers' side.
/// </summary>
internal class Broker(IConfiguration configuration)
{
    public const int MaxAttempts = 8;

    private readonly Dictionary<string, string[]> _subscriptions =
        configuration.GetSection("Subscriptions").Get<Dictionary<string, string[]>>() ?? [];
    private readonly List<Delivery> _pending = [];
    private readonly List<Delivery> _deadLetters = [];
    private readonly Lock _lock = new();
    private int _delivered;

    public int Publish(string topic, string messageId, string body)
    {
        var urls = _subscriptions.GetValueOrDefault(topic) ?? [];
        lock (_lock)
            _pending.AddRange(urls.Select(url => new Delivery(messageId, topic, url, body)));
        return urls.Length;
    }

    public List<Delivery> DueNow()
    {
        lock (_lock)
            return _pending.Where(l => l.NextAttempt <= DateTime.UtcNow).ToList();
    }

    public void Succeeded(Delivery delivery)
    {
        lock (_lock)
        {
            _pending.Remove(delivery);
            _delivered++;
        }
    }

    public void Failed(Delivery delivery, string error)
    {
        lock (_lock)
        {
            delivery.Attempts++;
            delivery.LastError = error;
            // Exponential backoff: 0.25 s, 0.5 s, 1 s, 2 s… capped at 30 s.
            var wait = Math.Min(0.25 * Math.Pow(2, delivery.Attempts - 1), 30);
            delivery.NextAttempt = DateTime.UtcNow.AddSeconds(wait);

            if (delivery.Attempts >= MaxAttempts)
            {
                _pending.Remove(delivery);
                _deadLetters.Add(delivery);
            }
        }
    }

    public object State()
    {
        lock (_lock)
            return new
            {
                delivered = _delivered,
                pending = _pending.Select(View).ToList(),
                deadLetters = _deadLetters.Select(View).ToList()
            };
    }

    private static object View(Delivery l) => new { l.Topic, l.Url, l.Attempts, l.LastError };
}

/// <summary>Background task that delivers the pending messages and retries on failure.</summary>
internal sealed class Dispatcher(Broker broker, IHttpClientFactory http, ILogger<Dispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            foreach (var delivery in broker.DueNow())
                await Deliver(delivery, stopping);
            await Task.Delay(TimeSpan.FromMilliseconds(200), stopping);
        }
    }

    private async Task Deliver(Delivery delivery, CancellationToken stopping)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, delivery.Url)
            {
                Content = new StringContent(delivery.Body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Message-Id", delivery.MessageId);

            var response = await http.CreateClient("subscribers").SendAsync(request, stopping);
            response.EnsureSuccessStatusCode();
            broker.Succeeded(delivery);
            logger.LogInformation("{Topic} delivered to {Url}.", delivery.Topic, delivery.Url);
        }
        catch (Exception e) when (!stopping.IsCancellationRequested)
        {
            broker.Failed(delivery, e.Message);
            logger.LogWarning("{Topic} not delivered to {Url} (attempt {Attempt}): {Error}",
                delivery.Topic, delivery.Url, delivery.Attempts, e.Message);
        }
    }
}
