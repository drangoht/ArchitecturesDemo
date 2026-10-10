using Microsoft.EntityFrameworkCore;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Infrastructure;

public class ComptesRepository(BanqueDbContext db) : IComptesRepository
{
    public async Task<Compte?> ParId(Guid id) => await db.Comptes.FindAsync(id);

    public async Task Ajouter(Compte compte) => await db.Comptes.AddAsync(compte);

    public async Task<bool> EstVide() => !await db.Comptes.AnyAsync();
}
