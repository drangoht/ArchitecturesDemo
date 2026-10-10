using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Micro.Virements;

/// <summary>Même relais que dans Micro.Comptes, volontairement dupliqué : aucun code partagé entre services.</summary>
internal sealed class RelaisOutbox(IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<RelaisOutbox> journal)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        while (!arret.IsCancellationRequested)
        {
            try
            {
                await EnvoyerLesMessagesEnAttente(arret);
            }
            catch (Exception e) when (!arret.IsCancellationRequested)
            {
                journal.LogWarning("Courtier injoignable ({Erreur}) : nouvel essai dans un instant.", e.Message);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), arret);
        }
    }

    private async Task EnvoyerLesMessagesEnAttente(CancellationToken arret)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirementsDb>();
        var enAttente = await db.Outbox.Where(m => m.EnvoyeLe == null).OrderBy(m => m.CreeLe).Take(20).ToListAsync(arret);
        if (enAttente.Count == 0)
            return;

        var courtier = http.CreateClient("courtier");
        foreach (var message in enAttente)
        {
            using var requete = new HttpRequestMessage(HttpMethod.Post, $"/sujets/{message.Sujet}")
            {
                Content = new StringContent(message.Corps, Encoding.UTF8, "application/json")
            };
            requete.Headers.Add("Message-Id", message.Id.ToString());

            (await courtier.SendAsync(requete, arret)).EnsureSuccessStatusCode();
            message.EnvoyeLe = DateTime.UtcNow;
            await db.SaveChangesAsync(arret);
            journal.LogInformation("Événement {Sujet} publié ({Id}).", message.Sujet, message.Id);
        }
    }
}
