using Microsoft.EntityFrameworkCore;
using Onion.Domain.Interfaces;
using Onion.Domain.Model;

namespace Onion.Infrastructure;

public class BanqueDbContext(DbContextOptions<BanqueDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Compte> Comptes => Set<Compte>();

    public Task Enregistrer() => SaveChangesAsync();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Le mapping est décrit ici, pas par des attributs dans le domaine : le domaine ignore EF Core.
        modelBuilder.Entity<Compte>(compte =>
        {
            compte.HasKey(c => c.Id);
            compte.Property(c => c.Titulaire).HasMaxLength(100);
        });
    }
}
