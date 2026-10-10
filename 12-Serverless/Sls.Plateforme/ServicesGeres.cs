using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sls.Plateforme;

public class OptionsPlateforme
{
    public int DemarrageAFroidMs { get; set; } = 600;
    public int MsAvantRecyclage { get; set; } = 20_000;
    public int InstancesMaxParFonction { get; set; } = 10;
    public int TentativesMax { get; set; } = 3;
    public int AttenteEntreTentativesMs { get; set; } = 500;
}

/// <summary>
/// Les services « gérés » du fournisseur : la fonction n'a pas de disque ni de mémoire durable,
/// tout ce qui doit survivre à une invocation va dans ces services.
/// </summary>
public sealed class ServicesGeres
{
    private readonly ConcurrentDictionary<string, TableGeree> _tables = new();

    public ServicesGeres(OptionsPlateforme options, IEnumerable<KeyValuePair<string, string?>>? reglages = null, ILogger? journal = null)
    {
        Journal = new Journal(journal);
        Files = new ServiceFiles(options);
        Stockage = new ServiceStockage(Files);
        foreach (var (cle, valeur) in reglages ?? [])
            if (valeur is not null)
                Reglages[cle] = valeur;
    }

    public Journal Journal { get; }
    public ServiceFiles Files { get; }
    public ServiceStockage Stockage { get; }

    /// <summary>Réglages de l'application (variables d'environnement des fonctions), modifiables sans redéployer.</summary>
    public ConcurrentDictionary<string, string> Reglages { get; } = new();

    public TableGeree Table(string nom) => _tables.GetOrAdd(nom, n => new TableGeree(n));
}

// ─── Base clé-valeur (façon DynamoDB / Azure Table Storage) ───

public record EntiteStockee(string Cle, string Donnees, long ETag);

/// <summary>
/// Chaque écriture incrémente l'ETag de l'entité. Une écriture conditionnelle (« seulement si l'ETag est encore n »)
/// permet à des dizaines d'instances parallèles de modifier la même donnée sans s'écraser.
/// </summary>
public sealed class TableGeree(string nom)
{
    private readonly Dictionary<string, EntiteStockee> _entites = [];
    private readonly Lock _verrou = new();

    public string Nom { get; } = nom;

    public Task<(T? Valeur, long ETag)> Lire<T>(string cle)
    {
        lock (_verrou)
            return Task.FromResult(_entites.TryGetValue(cle, out var e)
                ? (JsonSerializer.Deserialize<T>(e.Donnees, Json.Options), e.ETag)
                : (default(T), 0L));
    }

    /// <param name="etagAttendu">null : écriture sans condition ; 0 : l'entité ne doit pas exister ; n : elle doit être encore à l'ETag n.</param>
    public Task<long> Ecrire<T>(string cle, T valeur, long? etagAttendu = null)
    {
        lock (_verrou)
        {
            var actuel = _entites.TryGetValue(cle, out var e) ? e.ETag : 0;
            if (etagAttendu is not null && etagAttendu != actuel)
                throw new ConflitEcritureException(Nom, cle);
            _entites[cle] = new EntiteStockee(cle, JsonSerializer.Serialize(valeur, Json.Options), actuel + 1);
            return Task.FromResult(actuel + 1);
        }
    }

    public Task<IReadOnlyList<T>> Lister<T>(string prefixe = "")
    {
        lock (_verrou)
            return Task.FromResult<IReadOnlyList<T>>(_entites.Values
                .Where(e => e.Cle.StartsWith(prefixe, StringComparison.Ordinal))
                .OrderBy(e => e.Cle)
                .Select(e => JsonSerializer.Deserialize<T>(e.Donnees, Json.Options)!)
                .ToList());
    }
}

// ─── Files de messages (façon SQS / Azure Queue Storage) ───

public record MessageEnFile(string Id, string Corps, int Tentative, DateTime VisibleApres, string? DerniereErreur);

/// <summary>
/// Livraison « au moins une fois » : un message en échec est relivré après une attente croissante ;
/// après N échecs, il part dans la file « -poison » (les messages empoisonnés), où il attend qu'un humain s'en occupe.
/// </summary>
public sealed class ServiceFiles(OptionsPlateforme options)
{
    private readonly Dictionary<string, List<MessageEnFile>> _files = [];
    private readonly Lock _verrou = new();

    public Task Envoyer(string file, string corps)
    {
        Ajouter(file, new MessageEnFile(Guid.NewGuid().ToString("N"), corps, 0, DateTime.UtcNow, null));
        return Task.CompletedTask;
    }

    internal MessageEnFile? Prendre(string file)
    {
        lock (_verrou)
        {
            if (!_files.TryGetValue(file, out var messages))
                return null;
            var message = messages.FirstOrDefault(m => m.VisibleApres <= DateTime.UtcNow);
            if (message is not null)
                messages.Remove(message);
            return message;
        }
    }

    /// <summary>L'invocation a échoué : nouvel essai plus tard, ou file des messages empoisonnés.</summary>
    internal void Echec(string file, MessageEnFile message, string erreur)
    {
        var tentative = message.Tentative + 1;
        if (tentative >= options.TentativesMax)
            Ajouter($"{file}-poison", message with { Tentative = tentative, DerniereErreur = erreur });
        else
            Ajouter(file, message with
            {
                Tentative = tentative,
                DerniereErreur = erreur,
                VisibleApres = DateTime.UtcNow.AddMilliseconds(options.AttenteEntreTentativesMs * Math.Pow(2, tentative - 1))
            });
    }

    /// <summary>Pas d'instance disponible : le message est remis en file sans compter de tentative.</summary>
    internal void Remettre(string file, MessageEnFile message) =>
        Ajouter(file, message with { VisibleApres = DateTime.UtcNow.AddMilliseconds(100) });

    /// <summary>Renvoie les messages empoisonnés dans leur file d'origine (après correction du problème).</summary>
    public int Rejouer(string filePoison)
    {
        List<MessageEnFile> messages;
        lock (_verrou)
        {
            if (!_files.Remove(filePoison, out messages!))
                return 0;
        }
        var origine = filePoison.EndsWith("-poison") ? filePoison[..^"-poison".Length] : filePoison;
        foreach (var message in messages)
            Ajouter(origine, message with { Tentative = 0, VisibleApres = DateTime.UtcNow });
        return messages.Count;
    }

    public IReadOnlyDictionary<string, object> Etat()
    {
        lock (_verrou)
            return _files.Where(f => f.Value.Count > 0).ToDictionary(f => f.Key, f => (object)new
            {
                messages = f.Value.Count,
                dernieresErreurs = f.Value.Where(m => m.DerniereErreur is not null).Select(m => m.DerniereErreur).Distinct().Take(3)
            });
    }

    private void Ajouter(string file, MessageEnFile message)
    {
        lock (_verrou)
        {
            if (!_files.TryGetValue(file, out var messages))
                _files[file] = messages = [];
            messages.Add(message);
        }
    }
}

// ─── Stockage d'objets (façon S3 / Azure Blob Storage) ───

public sealed class ServiceStockage(ServiceFiles files)
{
    private readonly ConcurrentDictionary<string, string> _objets = new();
    private readonly List<(string Prefixe, string File)> _abonnements = [];

    internal void Abonner(string prefixe, string file) => _abonnements.Add((prefixe, file));

    public async Task Ecrire(string chemin, string contenu)
    {
        _objets[chemin] = contenu;
        foreach (var (prefixe, file) in _abonnements.Where(a => chemin.StartsWith(a.Prefixe, StringComparison.Ordinal)))
            await files.Envoyer(file, chemin);
    }

    public string? Lire(string chemin) => _objets.GetValueOrDefault(chemin);

    public IReadOnlyList<string> Lister(string prefixe = "") =>
        _objets.Keys.Where(c => c.StartsWith(prefixe, StringComparison.Ordinal)).Order().ToList();
}

// ─── Journal centralisé (façon CloudWatch / Application Insights) ───

public sealed class Journal(ILogger? journal)
{
    private readonly ConcurrentQueue<string> _lignes = new();

    public void Ecrire(string source, string message)
    {
        _lignes.Enqueue($"{DateTime.Now:HH:mm:ss.fff} [{source}] {message}");
        while (_lignes.Count > 500)
            _lignes.TryDequeue(out _);
        journal?.LogInformation("[{Source}] {Message}", source, message);
    }

    public IReadOnlyList<string> Dernieres(int nombre) => _lignes.Reverse().Take(nombre).Reverse().ToList();
}
