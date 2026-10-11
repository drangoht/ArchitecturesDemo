using System.Collections.Concurrent;

namespace Cell.Routeur;

/// <summary>Repère de l'assemblage, pour les tests.</summary>
public sealed class Marqueur;

/// <summary>Une cellule connue du plan de contrôle : où la joindre, combien de clients elle peut accueillir, quelle version y tourne.</summary>
public sealed record Cellule(string Id, string Url, int Capacite, bool Ouverte = true, string Version = "1.0");

public sealed class RegistreCellules
{
    private readonly ConcurrentDictionary<string, Cellule> _cellules = new(StringComparer.OrdinalIgnoreCase);

    public RegistreCellules(IEnumerable<Cellule> initiales)
    {
        foreach (var cellule in initiales)
            _cellules[cellule.Id] = cellule;
    }

    public IReadOnlyList<Cellule> Toutes() => _cellules.Values.OrderBy(c => c.Id).ToList();

    public Cellule? Trouver(string id) => _cellules.GetValueOrDefault(id);

    public bool Ajouter(Cellule cellule) => _cellules.TryAdd(cellule.Id, cellule);

    public void Remplacer(Cellule cellule) => _cellules[cellule.Id] = cellule;
}

/// <summary>
/// LA table du routeur : quel client vit dans quelle cellule. C'est tout ce que le routeur sait, et tout ce dont il a besoin.
/// En production : une petite base très disponible et répliquée (souvent mise en cache dans chaque instance du routeur),
/// écrite par le plan de contrôle seulement. Ici, en mémoire : redémarrer le routeur la reconstruit depuis les données de démo.
/// </summary>
public sealed class TablePlacement
{
    private readonly ConcurrentDictionary<string, string> _placement = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _enMigration = new(StringComparer.OrdinalIgnoreCase);

    public string? CelluleDe(string client) => _placement.GetValueOrDefault(client);

    public void Placer(string client, string cellule) => _placement[client] = cellule;

    public int Nombre(string cellule) => _placement.Values.Count(c => string.Equals(c, cellule, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string> Tout() => new SortedDictionary<string, string>(_placement);

    public bool EnMigration(string client) => _enMigration.ContainsKey(client);

    public void DebuterMigration(string client) => _enMigration[client] = 0;

    public void TerminerMigration(string client) => _enMigration.TryRemove(client, out _);
}
