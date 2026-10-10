using Hexa.Hexagone.Domaine;

namespace Hexa.Hexagone.Ports.Sortants;

// Ports sortants (« driven », secondaires) : ce dont l'application A BESOIN.
// L'hexagone définit l'interface ; les adapters sortants (mémoire, fichier JSON, SQL…) l'implémentent.

public interface IComptes
{
    Task<Compte?> Charger(Guid id);
    Task Sauvegarder(Compte compte);
}

public interface INotifications
{
    Task RetraitEffectue(Guid compteId, decimal montant);
    Task VirementEffectue(Guid sourceId, Guid cibleId, decimal montant);
}
