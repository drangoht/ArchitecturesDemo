using Microsoft.EntityFrameworkCore;

namespace Composants.Comptes;

internal class ComptesDbContext(DbContextOptions<ComptesDbContext> options) : DbContext(options)
{
    public DbSet<Compte> Comptes => Set<Compte>();
}
