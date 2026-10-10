namespace Composants.Comptes;

// L'implémentation est internal : on ne l'obtient que par l'interface, via l'injection de dépendances.
internal class ComptesComposant(ComptesDbContext db) : IComptesComposant
{
    public async Task<CompteDto> Ouvrir(string titulaire, decimal depotInitial, Guid? id = null)
    {
        var compte = new Compte(id ?? Guid.NewGuid(), titulaire, depotInitial);
        db.Comptes.Add(compte);
        await db.SaveChangesAsync();
        return compte.VersDto();
    }

    public async Task<CompteDto?> Consulter(Guid compteId) =>
        (await db.Comptes.FindAsync(compteId))?.VersDto();

    public Task<Resultat> Retirer(Guid compteId, decimal montant) =>
        Modifier(compteId, compte => compte.Retirer(montant));

    public Task<Resultat> Deposer(Guid compteId, decimal montant) =>
        Modifier(compteId, compte => compte.Deposer(montant));

    private async Task<Resultat> Modifier(Guid compteId, Func<Compte, Resultat> operation)
    {
        var compte = await db.Comptes.FindAsync(compteId);
        if (compte is null)
            return Resultat.Introuvable(compteId);

        var resultat = operation(compte);
        if (resultat.Succes)
            await db.SaveChangesAsync();
        return resultat;
    }
}
