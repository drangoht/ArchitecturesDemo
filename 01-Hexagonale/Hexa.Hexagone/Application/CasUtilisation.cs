using Hexa.Hexagone.Domaine;
using Hexa.Hexagone.Ports.Entrants;
using Hexa.Hexagone.Ports.Sortants;

namespace Hexa.Hexagone.Application;

// Chaque cas d'utilisation implémente un port entrant en ne s'appuyant que sur des ports sortants.

public class ConsulterCompte(IComptes comptes) : IConsulterCompte
{
    public async Task<CompteVue> Executer(Guid compteId)
    {
        var compte = await comptes.Charger(compteId) ?? throw new CompteIntrouvableException(compteId);
        return new CompteVue(compte.Id, compte.Titulaire, compte.Solde);
    }
}

public class RetirerArgent(IComptes comptes, INotifications notifications) : IRetirerArgent
{
    public async Task Executer(Guid compteId, decimal montant)
    {
        var compte = await comptes.Charger(compteId) ?? throw new CompteIntrouvableException(compteId);
        compte.Retirer(montant);
        await comptes.Sauvegarder(compte);
        await notifications.RetraitEffectue(compteId, montant);
    }
}

public class EffectuerVirement(IComptes comptes, INotifications notifications) : IEffectuerVirement
{
    public async Task Executer(Guid sourceId, Guid cibleId, decimal montant)
    {
        if (sourceId == cibleId)
            throw new RegleMetierException("Impossible de virer vers le même compte.");

        var source = await comptes.Charger(sourceId) ?? throw new CompteIntrouvableException(sourceId);
        var cible = await comptes.Charger(cibleId) ?? throw new CompteIntrouvableException(cibleId);

        source.Retirer(montant);
        cible.Deposer(montant);

        await comptes.Sauvegarder(source);
        await comptes.Sauvegarder(cible);
        await notifications.VirementEffectue(sourceId, cibleId, montant);
    }
}
