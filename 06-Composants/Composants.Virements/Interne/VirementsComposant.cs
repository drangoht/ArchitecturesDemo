using Composants.Comptes;
using Microsoft.EntityFrameworkCore;

namespace Composants.Virements;

internal class VirementsComposant(IComptesComposant comptes, VirementsDbContext db) : IVirementsComposant
{
    public async Task<Resultat> Virer(Guid sourceId, Guid cibleId, decimal montant)
    {
        if (sourceId == cibleId)
            return Resultat.Refuse("Impossible de virer vers le même compte.");

        // On ne touche jamais aux tables du composant Comptes : on passe par son interface.
        if (await comptes.Consulter(cibleId) is null)
            return Resultat.Introuvable(cibleId);

        var retrait = await comptes.Retirer(sourceId, montant);
        if (!retrait.Succes)
            return retrait;

        var depot = await comptes.Deposer(cibleId, montant);
        if (!depot.Succes)
        {
            // Pas de transaction commune entre deux composants qui ont chacun leur base :
            // si la 2e étape échoue, on compense la 1re.
            await comptes.Deposer(sourceId, montant);
            return depot;
        }

        db.Virements.Add(new Virement { SourceId = sourceId, CibleId = cibleId, Montant = montant });
        await db.SaveChangesAsync();
        return Resultat.Ok();
    }

    public async Task<IReadOnlyList<VirementDto>> Historique(Guid compteId) =>
        await db.Virements
            .Where(v => v.SourceId == compteId || v.CibleId == compteId)
            .OrderByDescending(v => v.DateUtc)
            .Select(v => new VirementDto(v.SourceId, v.CibleId, v.Montant, v.DateUtc))
            .ToListAsync();
}
