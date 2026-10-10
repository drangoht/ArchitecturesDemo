using System.Collections.Concurrent;
using Es.Domaine;

namespace Es.Stockage;

/// <summary>
/// Instantanés : simple cache de l'état d'un compte à une version donnée.
/// On peut les effacer à tout moment, rien n'est perdu : ils se recalculent à partir des événements.
/// </summary>
public class MagasinInstantanes
{
    private readonly ConcurrentDictionary<Guid, InstantaneCompte> _instantanes = new();

    public InstantaneCompte? Dernier(Guid compteId) => _instantanes.GetValueOrDefault(compteId);
    public void Enregistrer(InstantaneCompte instantane) => _instantanes[instantane.Id] = instantane;
    public void ToutEffacer() => _instantanes.Clear();
}

/// <summary>Charge un compte en rejouant ses événements, et enregistre ses nouveaux événements.</summary>
public class DepotComptes(IMagasinEvenements magasin, MagasinInstantanes instantanes)
{
    /// <summary>Un instantané est pris tous les N événements.</summary>
    public const int FrequenceInstantanes = 5;

    public async Task<Compte> Charger(Guid id)
    {
        var instantane = instantanes.Dernier(id);
        var suite = await magasin.LireFlux(id, apresVersion: instantane?.Version ?? 0);
        if (instantane is null && suite.Count == 0)
            throw new CompteIntrouvableException(id);
        return Compte.DepuisInstantane(instantane, suite.Select(e => e.Evenement()));
    }

    /// <summary>Voyage dans le temps : l'état du compte tel qu'il était à une version donnée.</summary>
    public async Task<Compte> ChargerALaVersion(Guid id, int version)
    {
        var historique = await magasin.LireFlux(id);
        if (historique.Count == 0)
            throw new CompteIntrouvableException(id);
        return Compte.Reconstituer(historique.Where(e => e.Version <= version).Select(e => e.Evenement()));
    }

    /// <summary>Voyage dans le temps : l'état du compte tel qu'il était à une date donnée.</summary>
    public async Task<Compte> ChargerALaDate(Guid id, DateTime date)
    {
        var historique = await magasin.LireFlux(id);
        if (historique.Count == 0)
            throw new CompteIntrouvableException(id);
        return Compte.Reconstituer(historique.Where(e => e.Date <= date).Select(e => e.Evenement()));
    }

    /// <summary>Enregistre les nouveaux événements de un ou plusieurs comptes, en tout ou rien.</summary>
    public async Task Enregistrer(params Compte[] comptes)
    {
        var aEnregistrer = comptes.Where(c => c.NouveauxEvenements.Count > 0).ToList();
        await magasin.Ajouter(aEnregistrer
            .Select(c => new AEcrire(c.Id, c.VersionChargee, c.NouveauxEvenements.ToList()))
            .ToArray());

        foreach (var compte in aEnregistrer)
        {
            compte.MarquerEnregistre();
            var dernierInstantane = instantanes.Dernier(compte.Id)?.Version ?? 0;
            if (compte.Version - dernierInstantane >= FrequenceInstantanes)
                instantanes.Enregistrer(compte.Instantane());
        }
    }
}
