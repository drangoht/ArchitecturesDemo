namespace Ddd.TenueDeCompte.Contrats;

/// <summary>Événement d'intégration publié à chaque virement (le montant en euros est fourni à la demande de la Conformité).</summary>
public record VirementEmis(
    Guid VirementId,
    Guid CompteSourceId,
    string IbanSource,
    string TitulaireSource,
    string IbanBeneficiaire,
    decimal Montant,
    string Devise,
    decimal MontantEnEuros,
    DateTime Date);

/// <summary>Implémenté par les contextes qui veulent être prévenus de ce qui se passe dans la Tenue de compte.</summary>
public interface IAbonneTenueDeCompte
{
    Task SurVirementEmis(VirementEmis evenement);
}

/// <summary>
/// Service hôte ouvert (Open Host Service) : ce que la Tenue de compte accepte de faire pour les autres contextes.
/// </summary>
public interface ITenueDeCompteApi
{
    Task BloquerCompte(Guid compteId, string motif);
}
