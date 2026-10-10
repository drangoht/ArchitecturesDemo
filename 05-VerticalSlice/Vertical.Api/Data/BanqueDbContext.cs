using Microsoft.EntityFrameworkCore;
using Vertical.Api.Domain;

namespace Vertical.Api.Data;

/// <summary>
/// Pas de repository par-dessus : le DbContext est déjà une abstraction (Unit of Work + Repository).
/// Chaque tranche l'utilise directement, à sa manière.
/// </summary>
public class BanqueDbContext(DbContextOptions<BanqueDbContext> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<Compte> Comptes => Set<Compte>();
    public DbSet<Operation> Operations => Set<Operation>();

    public void InitialiserDonneesDeDemo()
    {
        if (Comptes.Any())
            return;
        Comptes.AddRange(new Compte(Alice, "Alice", 1000m), new Compte(Bob, "Bob", 500m));
        SaveChanges();
    }
}
