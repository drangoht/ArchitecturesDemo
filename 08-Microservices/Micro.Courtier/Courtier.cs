using System.Text;

namespace Micro.Courtier;

/// <summary>Type repère du service (utilisé par les tests pour démarrer CE service).</summary>
public sealed class Marqueur;

internal class Livraison(string messageId, string sujet, string url, string corps)
{
    public string MessageId { get; } = messageId;
    public string Sujet { get; } = sujet;
    public string Url { get; } = url;
    public string Corps { get; } = corps;
    public int Tentatives { get; set; }
    public DateTime ProchainEssai { get; set; } = DateTime.UtcNow;
    public string? DerniereErreur { get; set; }
}

/// <summary>
/// File de livraisons en mémoire. Les abonnements sont déclarés dans appsettings.json (section « Abonnements »).
/// Garantie : « au moins une fois ». Un message peut être livré deux fois, d'où l'idempotence chez les abonnés.
/// </summary>
internal class Courtier(IConfiguration configuration)
{
    public const int TentativesMax = 8;

    private readonly Dictionary<string, string[]> _abonnements =
        configuration.GetSection("Abonnements").Get<Dictionary<string, string[]>>() ?? [];
    private readonly List<Livraison> _enAttente = [];
    private readonly List<Livraison> _lettresMortes = [];
    private readonly Lock _verrou = new();
    private int _livrees;

    public int Publier(string sujet, string messageId, string corps)
    {
        var urls = _abonnements.GetValueOrDefault(sujet) ?? [];
        lock (_verrou)
            _enAttente.AddRange(urls.Select(url => new Livraison(messageId, sujet, url, corps)));
        return urls.Length;
    }

    public List<Livraison> AEssayerMaintenant()
    {
        lock (_verrou)
            return _enAttente.Where(l => l.ProchainEssai <= DateTime.UtcNow).ToList();
    }

    public void Reussite(Livraison livraison)
    {
        lock (_verrou)
        {
            _enAttente.Remove(livraison);
            _livrees++;
        }
    }

    public void Echec(Livraison livraison, string erreur)
    {
        lock (_verrou)
        {
            livraison.Tentatives++;
            livraison.DerniereErreur = erreur;
            // Attente exponentielle : 0,25 s, 0,5 s, 1 s, 2 s… plafonnée à 30 s.
            var attente = Math.Min(0.25 * Math.Pow(2, livraison.Tentatives - 1), 30);
            livraison.ProchainEssai = DateTime.UtcNow.AddSeconds(attente);

            if (livraison.Tentatives >= TentativesMax)
            {
                _enAttente.Remove(livraison);
                _lettresMortes.Add(livraison);
            }
        }
    }

    public object Etat()
    {
        lock (_verrou)
            return new
            {
                livrees = _livrees,
                enAttente = _enAttente.Select(Vue).ToList(),
                lettresMortes = _lettresMortes.Select(Vue).ToList()
            };
    }

    private static object Vue(Livraison l) => new { l.Sujet, l.Url, l.Tentatives, l.DerniereErreur };
}

/// <summary>Tâche de fond qui livre les messages en attente et réessaie en cas d'échec.</summary>
internal sealed class Distributeur(Courtier courtier, IHttpClientFactory http, ILogger<Distributeur> journal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        while (!arret.IsCancellationRequested)
        {
            foreach (var livraison in courtier.AEssayerMaintenant())
                await Livrer(livraison, arret);
            await Task.Delay(TimeSpan.FromMilliseconds(200), arret);
        }
    }

    private async Task Livrer(Livraison livraison, CancellationToken arret)
    {
        try
        {
            using var requete = new HttpRequestMessage(HttpMethod.Post, livraison.Url)
            {
                Content = new StringContent(livraison.Corps, Encoding.UTF8, "application/json")
            };
            requete.Headers.Add("Message-Id", livraison.MessageId);

            var reponse = await http.CreateClient("abonnes").SendAsync(requete, arret);
            reponse.EnsureSuccessStatusCode();
            courtier.Reussite(livraison);
            journal.LogInformation("{Sujet} livré à {Url}.", livraison.Sujet, livraison.Url);
        }
        catch (Exception e) when (!arret.IsCancellationRequested)
        {
            courtier.Echec(livraison, e.Message);
            journal.LogWarning("{Sujet} non livré à {Url} (tentative {Tentative}) : {Erreur}",
                livraison.Sujet, livraison.Url, livraison.Tentatives, e.Message);
        }
    }
}
