using System.Diagnostics;
using System.Net.Http.Json;

namespace Bff.Web;

// The contracts of the services, copied here with ONLY the fields this screen needs ("tolerant reader"):
// a field added or removed elsewhere in the services does not break this BFF.
public sealed record Login(string User, string Password);
public sealed record TransferRequest(Guid Source, Guid Target, decimal Amount);

public sealed record DownstreamToken(string Token, int ExpiresIn);
public sealed record DownstreamProfile(string FirstName, string LastName, string Email, string Segment, string AdvisorId, Guid[] Accounts);
public sealed record DownstreamAccount(Guid Id, string Iban, string Label, string Type, string Currency, decimal Balance);
public sealed record DownstreamOperation(DateTimeOffset Date, string RawLabel, string Category, decimal Amount);
public sealed record DownstreamOperationsPage(int Page, int Size, int Total, DownstreamOperation[] Operations);
public sealed record DownstreamAdvisor(string Name, string Phone, string Email, string Branch);
public sealed record DownstreamOffer(string Title, string Text, string Image);
public sealed record DownstreamBalance(Guid Id, decimal Balance);

/// <summary>A downstream service answered with an error: the BFF translates it for its screen (see Program.cs).</summary>
public sealed class DownstreamServiceException(int status, string body) : Exception($"Downstream service: {status}")
{
    public int Status => status;
    public string Body => body;
}

/// <summary>How many calls to the services a browser request cost, and how long they took in total.</summary>
public sealed class DownstreamMetrics
{
    private int _calls;
    private long _ticks;

    public int Calls => _calls;
    public TimeSpan CumulativeDuration => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public void Record(TimeSpan duration)
    {
        Interlocked.Increment(ref _calls);
        Interlocked.Add(ref _ticks, duration.Ticks);
    }
}

/// <summary>
/// The HTTP client of the services, specific to THIS BFF: the mobile BFF has its own. Each call carries the user's token,
/// kept on the server side in their session: the browser never sees it.
/// </summary>
public sealed class ServicesClient(HttpClient http, DownstreamMetrics metrics)
{
    public async Task<DownstreamToken> GetToken(string user, string password)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Post, "/identity/token")
        {
            Content = JsonContent.Create(new { user, password })
        }, token: null);
        return (await response.Content.ReadFromJsonAsync<DownstreamToken>())!;
    }

    public Task<DownstreamProfile> Profile(string token) => Read<DownstreamProfile>("/profiles/me", token);

    public Task<DownstreamAccount> Account(Guid id, string token) => Read<DownstreamAccount>($"/accounts/{id}", token);

    public Task<DownstreamOperationsPage> Operations(Guid id, int size, string token) =>
        Read<DownstreamOperationsPage>($"/accounts/{id}/operations?page=1&size={size}", token);

    public Task<DownstreamAdvisor> Advisor(string id, string token) => Read<DownstreamAdvisor>($"/advisors/{id}", token);

    public Task<DownstreamOffer[]> Offers(string segment, string token, CancellationToken cancellation) =>
        Read<DownstreamOffer[]>($"/offers?segment={Uri.EscapeDataString(segment)}", token, cancellation);

    public async Task<DownstreamBalance> Transfer(Guid source, Guid target, decimal amount, string token)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { source, target, amount })
        }, token);
        return (await response.Content.ReadFromJsonAsync<DownstreamBalance>())!;
    }

    private async Task<T> Read<T>(string path, string token, CancellationToken cancellation = default)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Get, path), token, cancellation);
        return (await response.Content.ReadFromJsonAsync<T>(cancellation))!;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, string? token, CancellationToken cancellation = default)
    {
        using (request)
        {
            if (token is not null)
                request.Headers.Authorization = new("Bearer", token);
            var start = Stopwatch.GetTimestamp();
            var response = await http.SendAsync(request, cancellation);
            metrics.Record(Stopwatch.GetElapsedTime(start));
            if (response.IsSuccessStatusCode)
                return response;
            using (response)
                throw new DownstreamServiceException((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellation));
        }
    }
}
