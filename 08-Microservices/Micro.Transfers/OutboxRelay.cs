using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Micro.Transfers;

/// <summary>Same relay as in Micro.Accounts, deliberately duplicated: no code shared between services.</summary>
internal sealed class OutboxRelay(IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<OutboxRelay> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await SendPendingMessages(stopping);
            }
            catch (Exception e) when (!stopping.IsCancellationRequested)
            {
                logger.LogWarning("Broker unreachable ({Error}): trying again in a moment.", e.Message);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), stopping);
        }
    }

    private async Task SendPendingMessages(CancellationToken stopping)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TransfersDb>();
        var pending = await db.Outbox.Where(m => m.SentAt == null).OrderBy(m => m.CreatedAt).Take(20).ToListAsync(stopping);
        if (pending.Count == 0)
            return;

        var broker = http.CreateClient("broker");
        foreach (var message in pending)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/topics/{message.Topic}")
            {
                Content = new StringContent(message.Body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Message-Id", message.Id.ToString());

            (await broker.SendAsync(request, stopping)).EnsureSuccessStatusCode();
            message.SentAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stopping);
            logger.LogInformation("Event {Topic} published ({Id}).", message.Topic, message.Id);
        }
    }
}
