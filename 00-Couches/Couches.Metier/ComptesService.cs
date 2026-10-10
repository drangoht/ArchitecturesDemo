using Couches.Donnees;

namespace Couches.Metier;

/// <summary>
/// Service métier : toutes les règles sont ici et s'appliquent sur les entités de la couche données.
/// Style « Transaction Script » : une méthode = un cas d'utilisation, du début à la fin.
/// </summary>
public class ComptesService(IComptesDao dao)
{
    public const decimal PlafondParVirement = 5_000m;

    public async Task<CompteDto> Consulter(Guid id) => VersDto(await Charger(id));

    public async Task<CompteDto> Retirer(Guid id, decimal montant)
    {
        var compte = await Charger(id);
        VerifierRetraitPossible(compte, montant);

        compte.Solde -= montant;
        await dao.Enregistrer();
        return VersDto(compte);
    }

    public async Task Virer(Guid sourceId, Guid cibleId, decimal montant)
    {
        if (sourceId == cibleId)
            throw new RegleMetierException("Impossible de virer vers le même compte.");
        if (montant > PlafondParVirement)
            throw new RegleMetierException($"Un virement ne peut pas dépasser {PlafondParVirement:0} €.");

        var source = await Charger(sourceId);
        var cible = await Charger(cibleId);
        VerifierRetraitPossible(source, montant);

        source.Solde -= montant;
        cible.Solde += montant;
        await dao.Enregistrer();
    }

    // L'entité étant anémique, rien n'empêche une autre classe de faire « compte.Solde -= x »
    // sans passer par cette vérification : la règle n'est protégée que par la discipline.
    private static void VerifierRetraitPossible(CompteEntite compte, decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être strictement positif.");
        if (montant > compte.Solde)
            throw new RegleMetierException("Solde insuffisant.");
    }

    private async Task<CompteEntite> Charger(Guid id) =>
        await dao.ParId(id) ?? throw new CompteIntrouvableException(id);

    private static CompteDto VersDto(CompteEntite compte) => new(compte.Id, compte.Titulaire, compte.Solde);
}
