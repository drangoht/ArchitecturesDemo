using System.Net.Http.Json;

namespace Res.Bank;

public interface IFraudCheck
{
    Task<bool> IsApproved(Guid account, decimal amount, CancellationToken ct);
}

/// <summary>
/// A plain typed HttpClient: no retry, no timeout in sight. The strategies are handlers of its pipeline,
/// chosen where the client is registered (Program.cs of the API).
/// </summary>
public class FraudClient(HttpClient http) : IFraudCheck
{
    public async Task<bool> IsApproved(Guid account, decimal amount, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("check", new { account, amount }, ct);
        response.EnsureSuccessStatusCode();   // a 503 left after the last retry becomes an HttpRequestException
        var verdict = await response.Content.ReadFromJsonAsync<Verdict>(ct);
        return verdict!.Approved;
    }

    private record Verdict(bool Approved);
}
