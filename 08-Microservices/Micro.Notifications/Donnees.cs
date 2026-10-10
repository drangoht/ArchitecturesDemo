using Microsoft.EntityFrameworkCore;

namespace Micro.Notifications;

/// <summary>Type repère du service (utilisé par les tests pour démarrer CE service).</summary>
public sealed class Marqueur;

// Messages reçus : copies LOCALES des contrats JSON publiés par Comptes et Virements.
public record RetraitEffectue(Guid CompteId, decimal Montant, decimal NouveauSolde);
public record VirementEffectue(Guid VirementId, Guid SourceId, string TitulaireSource, Guid CibleId, string TitulaireCible, decimal Montant);

public class Notification(Guid compteId, string message)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompteId { get; private set; } = compteId;
    public string Message { get; private set; } = message;
    public DateTime DateUtc { get; private set; } = DateTime.UtcNow;
}

public class MessageTraite
{
    public Guid MessageId { get; set; }
}

public class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<MessageTraite> MessagesTraites => Set<MessageTraite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<MessageTraite>().HasKey(m => m.MessageId);
}
