using Clean.Entites;

namespace Clean.CasUtilisation.Passerelles;

/// <summary>
/// Gateway : l'accès aux données dont les cas d'utilisation ont besoin.
/// Défini ici, implémenté dans le cercle des Interface Adapters.
/// </summary>
public interface IComptesPasserelle
{
    Task<Compte?> Charger(Guid id);
    Task Sauvegarder(Compte compte);
}
