using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Application;

/// <summary>
/// Service applicatif : charge, délègue au domaine, enregistre.
/// Il ne contient aucune règle métier ; il coordonne.
/// </summary>
public class ComptesAppService(IComptesRepository comptes, IUnitOfWork unitOfWork)
{
    public async Task<CompteDto> Consulter(Guid id) =>
        CompteDto.Depuis(await comptes.ObtenirOuEchouer(id));

    public async Task<CompteDto> Retirer(Guid id, decimal montant)
    {
        var compte = await comptes.ObtenirOuEchouer(id);
        compte.Retirer(montant);
        await unitOfWork.Enregistrer();
        return CompteDto.Depuis(compte);
    }
}

internal static class ComptesRepositoryExtensions
{
    public static async Task<Compte> ObtenirOuEchouer(this IComptesRepository comptes, Guid id) =>
        await comptes.ParId(id) ?? throw new CompteIntrouvableException(id);
}
