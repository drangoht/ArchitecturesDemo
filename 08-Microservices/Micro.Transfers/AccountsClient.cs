using System.Net;
using System.Text.Json;

namespace Micro.Transfers;

// LOCAL representation of an account: no library shared with the Accounts service,
// the contract between the two is the JSON they exchange.
internal record RemoteAccount(Guid Id, string Owner, decimal Balance);

internal enum Outcome { Accepted, Refused, NotFound }

internal record MovementResponse(Outcome Outcome, string? Error = null);

/// <summary>HTTP client of the Accounts service. A network failure surfaces as an exception (after the retries).</summary>
internal class AccountsClient(IHttpClientFactory factory)
{
    private HttpClient Http => factory.CreateClient("accounts");

    public async Task<RemoteAccount?> Get(Guid id)
    {
        var response = await Http.GetAsync($"/accounts/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RemoteAccount>();
    }

    public Task<MovementResponse> Debit(Guid id, decimal amount, string operationId) =>
        Movement(id, "debits", amount, operationId);

    public Task<MovementResponse> Credit(Guid id, decimal amount, string operationId) =>
        Movement(id, "credits", amount, operationId);

    private async Task<MovementResponse> Movement(Guid id, string type, decimal amount, string operationId)
    {
        var response = await Http.PostAsJsonAsync($"/internal/accounts/{id}/{type}", new { operationId, amount });
        return response.StatusCode switch
        {
            HttpStatusCode.OK => new MovementResponse(Outcome.Accepted),
            HttpStatusCode.NotFound => new MovementResponse(Outcome.NotFound, $"Account {id} not found."),
            HttpStatusCode.BadRequest => new MovementResponse(Outcome.Refused, await ReadError(response)),
            _ => throw new HttpRequestException($"The Accounts service answered {(int)response.StatusCode}.")
        };
    }

    private static async Task<string?> ReadError(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
}
