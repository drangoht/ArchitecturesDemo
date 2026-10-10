using Onion.Domain.Model;

namespace Onion.Domain.Services;

/// <summary>
/// Couche 2 — Domain Services : une règle métier qui concerne plusieurs entités
/// et n'appartient naturellement à aucune d'elles. Pure logique : aucun accès aux données.
/// </summary>
public class ServiceVirement
{
    public const decimal PlafondParVirement = 5_000m;

    public void Virer(Compte source, Compte cible, decimal montant)
    {
        if (source.Id == cible.Id)
            throw new RegleMetierException("Impossible de virer vers le même compte.");
        if (montant > PlafondParVirement)
            throw new RegleMetierException($"Un virement ne peut pas dépasser {PlafondParVirement:0} €.");

        source.Retirer(montant);
        cible.Deposer(montant);
    }
}
