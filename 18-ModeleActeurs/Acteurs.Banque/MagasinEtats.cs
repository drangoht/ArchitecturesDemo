using System.Collections.Concurrent;
using System.Text.Json;

namespace Acteurs.Banque;

/// <summary>
/// Le stockage DURABLE des acteurs (une base de données, en vrai) : une ligne par acteur, écrite par lui seul.
/// L'état est sérialisé en JSON, donc copié : ce qu'un acteur a en mémoire et ce qui est stocké sont deux choses distinctes.
/// Tant qu'il n'a pas écrit, une modification n'existe que dans son incarnation, et disparaît avec elle si elle plante.
/// </summary>
public sealed class MagasinEtats
{
    private readonly ConcurrentDictionary<string, string> _lignes = new();

    public Task<T?> Lire<T>(string chemin) where T : class =>
        Task.FromResult(_lignes.TryGetValue(chemin, out var json) ? JsonSerializer.Deserialize<T>(json) : null);

    public Task Ecrire<T>(string chemin, T etat)
    {
        _lignes[chemin] = JsonSerializer.Serialize(etat);
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<string> Chemins() => _lignes.Keys.Order().ToList();
}
