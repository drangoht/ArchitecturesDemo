using System.Text.Json;
using Hexa.Hexagone.Domaine;
using Hexa.Hexagone.Ports.Sortants;

namespace Hexa.Adapters.Persistance;

/// <summary>
/// Adapter sortant qui stocke les comptes dans un fichier JSON.
/// Même port que <see cref="ComptesEnMemoire"/> : l'hexagone ne voit aucune différence.
/// </summary>
public class ComptesFichierJson : IComptes
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _chemin;
    private readonly SemaphoreSlim _verrou = new(1, 1);

    public ComptesFichierJson(string chemin, IEnumerable<Compte>? comptesSiFichierAbsent = null)
    {
        _chemin = chemin;
        if (!File.Exists(chemin))
            Ecrire((comptesSiFichierAbsent ?? Enumerable.Empty<Compte>()).Select(Ligne.Depuis).ToList());
    }

    public async Task<Compte?> Charger(Guid id)
    {
        await _verrou.WaitAsync();
        try
        {
            return Lire().FirstOrDefault(l => l.Id == id)?.VersCompte();
        }
        finally
        {
            _verrou.Release();
        }
    }

    public async Task Sauvegarder(Compte compte)
    {
        await _verrou.WaitAsync();
        try
        {
            var lignes = Lire();
            lignes.RemoveAll(l => l.Id == compte.Id);
            lignes.Add(Ligne.Depuis(compte));
            Ecrire(lignes);
        }
        finally
        {
            _verrou.Release();
        }
    }

    private List<Ligne> Lire() =>
        JsonSerializer.Deserialize<List<Ligne>>(File.ReadAllText(_chemin)) ?? [];

    private void Ecrire(List<Ligne> lignes) =>
        File.WriteAllText(_chemin, JsonSerializer.Serialize(lignes.OrderBy(l => l.Titulaire), Options));

    // Format de stockage propre à l'adapter : le domaine ignore qu'il est sérialisé.
    private record Ligne(Guid Id, string Titulaire, decimal Solde)
    {
        public static Ligne Depuis(Compte compte) => new(compte.Id, compte.Titulaire, compte.Solde);
        public Compte VersCompte() => new(Id, Titulaire, Solde);
    }
}
