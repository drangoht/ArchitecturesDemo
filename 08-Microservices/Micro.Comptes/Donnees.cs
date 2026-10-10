using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Micro.Comptes;

/// <summary>Type repère du service (utilisé par les tests pour démarrer CE service).</summary>
public sealed class Marqueur;

public class Compte(Guid id, string titulaire, decimal solde)
{
    public Guid Id { get; private set; } = id;
    public string Titulaire { get; private set; } = titulaire;
    public decimal Solde { get; private set; } = solde;

    /// <returns>null si le mouvement est fait, sinon la raison du refus.</returns>
    public string? Debiter(decimal montant)
    {
        if (montant <= 0) return "Le montant doit être strictement positif.";
        if (montant > Solde) return "Solde insuffisant.";
        Solde -= montant;
        return null;
    }

    public string? Crediter(decimal montant)
    {
        if (montant <= 0) return "Le montant doit être strictement positif.";
        Solde += montant;
        return null;
    }
}

/// <summary>Mémoire des opérations déjà traitées, pour rendre les appels entre services idempotents.</summary>
public class OperationTraitee
{
    public string OperationId { get; set; } = "";
    public bool Acceptee { get; set; }
    public string? Erreur { get; set; }
    public decimal SoldeApres { get; set; }
}

/// <summary>
/// Outbox : le message à publier est enregistré dans la MÊME transaction que la modification métier.
/// Un relais l'envoie ensuite au courtier. Si le courtier est en panne, rien n'est perdu.
/// </summary>
public class MessageOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sujet { get; set; } = "";
    public string Corps { get; set; } = "";
    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public DateTime? EnvoyeLe { get; set; }

    public static MessageOutbox Creer(string sujet, object contenu) =>
        new() { Sujet = sujet, Corps = JsonSerializer.Serialize(contenu, Json) };
}

public class ComptesDb(DbContextOptions<ComptesDb> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<Compte> Comptes => Set<Compte>();
    public DbSet<OperationTraitee> OperationsTraitees => Set<OperationTraitee>();
    public DbSet<MessageOutbox> Outbox => Set<MessageOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OperationTraitee>().HasKey(o => o.OperationId);

    public void InitialiserDonneesDeDemo()
    {
        if (Comptes.Any())
            return;
        Comptes.AddRange(new Compte(Alice, "Alice", 1000m), new Compte(Bob, "Bob", 500m));
        SaveChanges();
    }
}
