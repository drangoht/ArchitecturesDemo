using Onion.Domain.Interfaces;
using Onion.Domain.Services;

namespace Onion.Application;

public record VirementResultat(CompteDto Source, CompteDto Cible);

public class VirementsAppService(IComptesRepository comptes, IUnitOfWork unitOfWork, ServiceVirement serviceVirement)
{
    public async Task<VirementResultat> Virer(Guid sourceId, Guid cibleId, decimal montant)
    {
        var source = await comptes.ObtenirOuEchouer(sourceId);
        var cible = await comptes.ObtenirOuEchouer(cibleId);

        // La règle (plafond, même compte, solde…) vit dans le service de domaine.
        serviceVirement.Virer(source, cible, montant);

        // Une seule validation pour les deux comptes : tout ou rien.
        await unitOfWork.Enregistrer();
        return new VirementResultat(CompteDto.Depuis(source), CompteDto.Depuis(cible));
    }
}
