using Microsoft.EntityFrameworkCore;

namespace Couches.Donnees;

// L'interface est définie PAR la couche données, dans SON vocabulaire (CompteEntite).
// Différence clé avec Onion/Hexagonale : là-bas, c'est le métier qui définit l'interface dont il a besoin.

public interface IComptesDao
{
    Task<CompteEntite?> ParId(Guid id);
    Task Ajouter(CompteEntite compte);
    Task<bool> EstVide();
    Task Enregistrer();
}

public class ComptesDao(BanqueDbContext db) : IComptesDao
{
    public async Task<CompteEntite?> ParId(Guid id) => await db.Comptes.FindAsync(id);

    public async Task Ajouter(CompteEntite compte) => await db.Comptes.AddAsync(compte);

    public async Task<bool> EstVide() => !await db.Comptes.AnyAsync();

    public Task Enregistrer() => db.SaveChangesAsync();
}
