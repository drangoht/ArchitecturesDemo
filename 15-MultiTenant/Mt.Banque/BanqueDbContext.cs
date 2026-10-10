using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Mt.Locataires;

namespace Mt.Banque;

/// <summary>
/// Le garde-fou de l'isolation. Trois protections, pour que le métier n'ait jamais à penser au locataire :
/// ① un filtre global ajoute « WHERE LocataireId = @courant » à TOUTES les requêtes ;
/// ② chaque insertion est estampillée avec le locataire courant ;
/// ③ modifier ou supprimer une ligne d'un autre locataire lève une exception.
/// Sans locataire, toute requête échoue (<see cref="AucunLocataireException"/>) : on ferme par défaut.
/// </summary>
public sealed class BanqueDbContext(DbContextOptions<BanqueDbContext> options, IContexteLocataire contexte) : DbContext(options)
{
    public DbSet<Compte> Comptes => Set<Compte>();
    public DbSet<Operation> Operations => Set<Operation>();

    /// <summary>
    /// Membre du DbContext : EF le relit à CHAQUE requête. Le modèle (et son filtre) est construit une seule fois
    /// pour toute l'application, mais chaque instance de DbContext filtre sur son propre locataire.
    /// </summary>
    private string LocataireCourant => contexte.Exiger().Id;

    protected override void OnModelCreating(ModelBuilder modele)
    {
        modele.Entity<Compte>();
        modele.Entity<Operation>().HasIndex(o => o.Cle);

        // ① Appliqué à toutes les entités qui implémentent IAppartientAUnLocataire, découvertes par réflexion :
        //   une nouvelle entité est protégée sans qu'on ait à y penser (et un test le vérifie).
        var methode = typeof(BanqueDbContext).GetMethod(nameof(Isoler), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in modele.Model.GetEntityTypes().Select(t => t.ClrType).Where(typeof(IAppartientAUnLocataire).IsAssignableFrom).ToList())
            methode.MakeGenericMethod(type).Invoke(this, [modele]);
    }

    private void Isoler<T>(ModelBuilder modele) where T : class, IAppartientAUnLocataire
    {
        // Clé composite : deux locataires peuvent avoir un compte de même identifiant (ici, Alice et Bob existent partout)
        // sans conflit dans la base partagée.
        modele.Entity<T>().HasKey(nameof(IAppartientAUnLocataire.LocataireId), "Id");
        modele.Entity<T>().HasQueryFilter(e => e.LocataireId == LocataireCourant);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Estampiller();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken annulation = default)
    {
        Estampiller();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, annulation);
    }

    private void Estampiller()
    {
        foreach (var entree in ChangeTracker.Entries<IAppartientAUnLocataire>())
        {
            if (entree.State == EntityState.Added && entree.Entity.LocataireId == "")
            {
                // ② L'insertion prend le locataire courant.
                entree.Property(nameof(IAppartientAUnLocataire.LocataireId)).CurrentValue = LocataireCourant;
            }
            else if (entree.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                     && entree.Entity.LocataireId != LocataireCourant)
            {
                // ③ Une entité d'un autre locataire n'a rien à faire ici.
                throw new FuiteEntreLocatairesException(
                    $"{entree.Entity.GetType().Name} du locataire « {entree.Entity.LocataireId} » modifié pour le compte de « {LocataireCourant} ».");
            }
        }
    }
}
