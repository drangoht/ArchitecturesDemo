using Ddd.TenueDeCompte.Application;
using Ddd.TenueDeCompte.Domaine.Comptes;
using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;
using Microsoft.EntityFrameworkCore;

namespace Ddd.TenueDeCompte.Infrastructure;

/// <summary>
/// Le mapping vit ici, pas dans le domaine : les objets-valeurs sont convertis pour la base.
/// (Avec SQL Server on préférerait ComplexProperty/OwnsOne pour Argent, afin de pouvoir requêter sur le montant.)
/// </summary>
public class TenueDeCompteDb(DbContextOptions<TenueDeCompteDb> options) : DbContext(options), IUniteDeTravail
{
    public DbSet<Compte> Comptes => Set<Compte>();

    public Task Valider() => SaveChangesAsync();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Compte>(compte =>
        {
            compte.HasKey(c => c.Id);
            compte.Property(c => c.Id).HasConversion(id => id.Valeur, valeur => new CompteId(valeur));
            compte.Property(c => c.Iban).HasConversion(iban => iban.Valeur, valeur => Iban.Depuis(valeur));
            compte.Property(c => c.Titulaire).HasConversion(nom => nom.Valeur, valeur => NomTitulaire.Depuis(valeur));
            compte.Property(c => c.Devise).HasConversion(devise => devise.Code, code => Devise.De(code));
            compte.Property(c => c.Solde).HasConversion(argent => argent.VersTexte(), texte => Argent.DepuisTexte(texte));
            compte.Property(c => c.PlafondRetraitJournalier).HasConversion(argent => argent.VersTexte(), texte => Argent.DepuisTexte(texte));
            compte.Ignore(c => c.EvenementsDomaine);

            // Les mouvements appartiennent à l'agrégat : ils sont chargés et enregistrés avec lui.
            compte.OwnsMany(c => c.Mouvements, mouvement =>
            {
                mouvement.WithOwner().HasForeignKey("CompteId");
                mouvement.HasKey(m => m.Id);
                mouvement.Property(m => m.Id).ValueGeneratedNever();
                mouvement.Property(m => m.Montant).HasConversion(argent => argent.VersTexte(), texte => Argent.DepuisTexte(texte));
            });
            compte.Navigation(c => c.Mouvements).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

public class DepotComptes(TenueDeCompteDb db) : IComptes
{
    public Task<Compte?> ParId(CompteId id) => db.Comptes.FirstOrDefaultAsync(c => c.Id == id);

    public Task<Compte?> ParIban(Iban iban) => db.Comptes.FirstOrDefaultAsync(c => c.Iban == iban);

    public async Task<IReadOnlyList<Compte>> Tous() => await db.Comptes.ToListAsync();

    public async Task Ajouter(Compte compte) => await db.Comptes.AddAsync(compte);

    public async Task<long> ProchainNumeroDeCompte() =>
        await db.Comptes.CountAsync() + db.ChangeTracker.Entries<Compte>().Count(e => e.State == EntityState.Added) + 1;
}
