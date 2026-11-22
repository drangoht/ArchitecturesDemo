using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Res.FraudSimulator;

public enum PartnerMode { Healthy, Flaky, Slow, Down }

/// <summary>
/// The partner's fraud-scoring service, simulated: its behaviour is chosen from outside (POST /partner/mode)
/// and it counts the calls it receives, to see what the strategies spare it.
/// </summary>
public class FraudPartner(TimeProvider clock)
{
    public static readonly TimeSpan NormalLatency = TimeSpan.FromMilliseconds(20);
    public static readonly TimeSpan SlowLatency = TimeSpan.FromSeconds(3);
    public const decimal RefusedFrom = 800m;

    private readonly Lock _gate = new();
    private int _failuresLeft;
    private int _inProgress;

    public PartnerMode Mode { get; private set; } = PartnerMode.Healthy;
    public int CallsReceived { get; private set; }
    public int MaxInProgress { get; private set; }

    /// <summary>Flaky: the next <paramref name="failures"/> calls answer 503, then it is healthy again.</summary>
    public void Set(PartnerMode mode, int failures = 2)
    {
        lock (_gate)
        {
            Mode = mode;
            _failuresLeft = mode == PartnerMode.Flaky ? failures : 0;
        }
    }

    internal async Task<HttpResponseMessage> Answer(FraudCheckRequest check, CancellationToken ct)
    {
        PartnerMode mode;
        bool fail;
        lock (_gate)
        {
            CallsReceived++;
            MaxInProgress = Math.Max(MaxInProgress, ++_inProgress);
            mode = Mode;
            fail = mode == PartnerMode.Down || (mode == PartnerMode.Flaky && _failuresLeft-- > 0);
        }
        try
        {
            await Task.Delay(mode == PartnerMode.Slow ? SlowLatency : NormalLatency, clock, ct);
            if (fail)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new FraudCheckResponse(check.Amount < RefusedFrom))
            };
        }
        finally
        {
            lock (_gate)
                _inProgress--;
        }
    }
}

public record FraudCheckRequest(Guid Account, decimal Amount);

public record FraudCheckResponse(bool Approved);

/// <summary>Stands in for the network: the last handler of the HttpClient pipeline, which answers in place of the partner.</summary>
public class SimulatedFraudHandler(FraudPartner partner) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // The retry sends the SAME request again: its body must stay readable. Read as text, the buffered
        // content stays intact; ReadFromJsonAsync would close its stream and break the next attempt.
        var body = await request.Content!.ReadAsStringAsync(ct);
        var check = JsonSerializer.Deserialize<FraudCheckRequest>(body, JsonSerializerOptions.Web);
        return await partner.Answer(check!, ct);
    }
}
