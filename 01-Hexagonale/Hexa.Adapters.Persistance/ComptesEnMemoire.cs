using System.Collections.Concurrent;
using Hexa.Hexagone.Domaine;
using Hexa.Hexagone.Ports.Sortants;

namespace Hexa.Adapters.Persistance;

/// <summary>
/// Adapter sortant en mémoire : idéal pour les tests et les démos.
/// On stocke des copies, comme une vraie base : une modification non sauvegardée n'est pas visible.
/// </summary>
public class ComptesEnMemoire : IComptes
{
    private readonly ConcurrentDictionary<Guid, (string Titulaire, decimal Solde)> _lignes = new();

    public ComptesEnMemoire(IEnumerable<Compte>? comptesInitiaux = null)
    {
        foreach (var compte in comptesInitiaux ?? Enumerable.Empty<Compte>())
            _lignes[compte.Id] = (compte.Titulaire, compte.Solde);
    }

    public Task<Compte?> Charger(Guid id) =>
        Task.FromResult(_lignes.TryGetValue(id, out var ligne)
            ? new Compte(id, ligne.Titulaire, ligne.Solde)
            : null);

    public Task Sauvegarder(Compte compte)
    {
        _lignes[compte.Id] = (compte.Titulaire, compte.Solde);
        return Task.CompletedTask;
    }
}
