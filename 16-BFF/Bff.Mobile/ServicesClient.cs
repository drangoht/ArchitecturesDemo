using System.Diagnostics;
using System.Net.Http.Json;

namespace Bff.Mobile;

/// <summary>Marker of the assembly, for the tests.</summary>
public sealed class Marker;

// Deliberate copy, and not a project shared with the web BFF: each BFF belongs to the team of ITS application
// and evolves at its own pace. Only the fields useful to the mobile application are read.
public sealed record Login(string User, string Password);
public sealed record TransferRequest(Guid Source, Guid Target, decimal Amount);

public sealed record DownstreamToken(string Token, int ExpiresIn);
public sealed record DownstreamProfile(string FirstName, string Segment, Guid[] Accounts);
public sealed record DownstreamAccount(Guid Id, string Label, decimal Balance);
public sealed record DownstreamOperation(DateTimeOffset Date, string RawLabel, string Category, decimal Amount);
public sealed record DownstreamOperationsPage(int Page, int Size, int Total, DownstreamOperation[] Operations);
public sealed record DownstreamOffer(string Title);
public sealed record DownstreamBalance(Guid Id, decimal Balance);

public sealed class DownstreamServiceException(int status, string body) : Exception($"Downstream service: {status}")
{
    public int Status => status;
    public string Body => body;
}

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

/// <summary>The token comes from the mobile application itself (the phone's secure storage): the BFF passes it on.</summary>
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

    public Task<DownstreamOperationsPage> Operations(Guid id, int page, int size, string token) =>
        Read<DownstreamOperationsPage>($"/accounts/{id}/operations?page={page}&size={size}", token);

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
