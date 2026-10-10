using Cqrs.Ecriture.Domaine;
using Cqrs.Evenements;
using Microsoft.EntityFrameworkCore;

namespace Cqrs.Ecriture.Persistance;

public class EvenementStocke
{
    public long Sequence { get; set; }
    public string Type { get; set; } = "";
    public string Donnees { get; set; } = "";
    public DateTime Date { get; set; }
}

/// <summary>
/// Base du côté écriture : l'état des agrégats + le journal des événements, enregistrés dans la même transaction.
/// </summary>
public class EcritureDb(DbContextOptions<EcritureDb> options) : DbContext(options)
{
    public DbSet<Compte> Comptes => Set<Compte>();
    public DbSet<EvenementStocke> Evenements => Set<EvenementStocke>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Compte>(compte =>
        {
            compte.HasKey(c => c.Id);
            compte.Ignore(c => c.Evenements);
        });
        modelBuilder.Entity<EvenementStocke>(evenement =>
        {
            evenement.HasKey(e => e.Sequence);
            evenement.Property(e => e.Sequence).ValueGeneratedOnAdd();
        });
    }

    /// <summary>Enregistre les modifications et ajoute au journal les événements produits par les agrégats.</summary>
    public async Task Enregistrer(params IEvenementDomaine[] evenementsSupplementaires)
    {
        var evenements = ChangeTracker.Entries<Compte>()
            .SelectMany(entree => entree.Entity.ExtraireEvenements())
            .Concat(evenementsSupplementaires)
            .ToList();

        foreach (var evenement in evenements)
            Evenements.Add(new EvenementStocke
            {
                Type = evenement.GetType().Name,
                Donnees = CatalogueEvenements.Serialiser(evenement),
                Date = evenement.Date
            });

        await SaveChangesAsync();
    }
}

internal class FluxEvenements(EcritureDb db) : IFluxEvenements
{
    public async Task<IReadOnlyList<EvenementEnregistre>> LireApres(long sequence, int maximum) =>
        (await db.Evenements.AsNoTracking()
            .Where(e => e.Sequence > sequence)
            .OrderBy(e => e.Sequence)
            .Take(maximum)
            .ToListAsync())
        .Select(e => new EvenementEnregistre(e.Sequence, CatalogueEvenements.Deserialiser(e.Type, e.Donnees)))
        .ToList();

    public async Task<long> DerniereSequence() =>
        await db.Evenements.AnyAsync() ? await db.Evenements.MaxAsync(e => e.Sequence) : 0;
}
