using Fonctionnel.Coeur;
using Microsoft.EntityFrameworkCore;

namespace Fonctionnel.Coquille.Donnees;

// Modèle de persistance mutable, propre à la coquille. Le cœur ne le voit jamais.

public class CompteLigne
{
    public Guid Id { get; set; }
    public string Titulaire { get; set; } = "";
    public decimal Solde { get; set; }

    public Compte VersCoeur() => new(Id, Titulaire, Solde);
}

public class OperationLigne
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompteId { get; set; }
    public string Type { get; set; } = "";
    public decimal Montant { get; set; }
    public DateTime DateUtc { get; set; }
}

public class BanqueDbContext(DbContextOptions<BanqueDbContext> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<CompteLigne> Comptes => Set<CompteLigne>();
    public DbSet<OperationLigne> Operations => Set<OperationLigne>();

    public void InitialiserDonneesDeDemo()
    {
        if (Comptes.Any())
            return;
        Comptes.AddRange(
            new CompteLigne { Id = Alice, Titulaire = "Alice", Solde = 5000m },
            new CompteLigne { Id = Bob, Titulaire = "Bob", Solde = 500m });
        SaveChanges();
    }
}
