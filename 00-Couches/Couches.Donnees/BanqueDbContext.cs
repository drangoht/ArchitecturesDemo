using Microsoft.EntityFrameworkCore;

namespace Couches.Donnees;

public class BanqueDbContext(DbContextOptions<BanqueDbContext> options) : DbContext(options)
{
    public DbSet<CompteEntite> Comptes => Set<CompteEntite>();
}
