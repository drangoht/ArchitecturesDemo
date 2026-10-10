using System.Collections.Concurrent;

namespace Mt.Locataires;

/// <summary>
/// Le registre des locataires. En production, une table de la plateforme (souvent dans une base « catalogue » à part),
/// avec un cache : il est lu à chaque requête.
/// </summary>
public sealed class CatalogueLocataires
{
    private readonly ConcurrentDictionary<string, Locataire> _locataires = new(StringComparer.OrdinalIgnoreCase);

    public CatalogueLocataires(params IEnumerable<Locataire> initiaux)
    {
        foreach (var locataire in initiaux)
            if (!Ajouter(locataire))
                throw new InvalidOperationException($"Locataire en double ou identifiant invalide : « {locataire.Id} ».");
    }

    public Locataire? Trouver(string id) => _locataires.GetValueOrDefault(id);

    public IReadOnlyList<Locataire> Tous() => _locataires.Values.OrderBy(l => l.Id).ToList();

    public bool Ajouter(Locataire locataire) =>
        Locataire.IdentifiantValide(locataire.Id) && _locataires.TryAdd(locataire.Id, locataire);

    public void Remplacer(Locataire locataire) => _locataires[locataire.Id] = locataire;

    public bool Retirer(string id) => _locataires.TryRemove(id, out _);
}
