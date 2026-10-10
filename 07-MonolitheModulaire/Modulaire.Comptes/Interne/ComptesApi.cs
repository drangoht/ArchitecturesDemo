using Modulaire.Comptes.Contrats;

namespace Modulaire.Comptes;

/// <summary>Implémentation (internal) de l'API publique du module, utilisée par les autres modules.</summary>
internal class ComptesApi(ComptesDbContext db) : IComptesApi
{
    public async Task<CompteInfo?> Consulter(Guid compteId) =>
        await db.Comptes.FindAsync(compteId) is { } c ? new CompteInfo(c.Id, c.Titulaire, c.Solde) : null;

    public Task<ResultatOperation> Debiter(Guid compteId, decimal montant) =>
        Modifier(compteId, compte => compte.Debiter(montant));

    public Task<ResultatOperation> Crediter(Guid compteId, decimal montant) =>
        Modifier(compteId, compte => compte.Crediter(montant));

    private async Task<ResultatOperation> Modifier(Guid compteId, Func<Compte, ResultatOperation> operation)
    {
        var compte = await db.Comptes.FindAsync(compteId);
        if (compte is null)
            return ResultatOperation.Introuvable(compteId);

        var resultat = operation(compte);
        if (resultat.Succes)
            await db.SaveChangesAsync();
        return resultat;
    }
}
