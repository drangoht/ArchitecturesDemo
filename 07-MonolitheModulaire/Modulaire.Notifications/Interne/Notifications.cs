using Microsoft.EntityFrameworkCore;
using Modulaire.Comptes.Contrats;
using Modulaire.Partage;
using Modulaire.Virements.Contrats;

namespace Modulaire.Notifications;

internal class Notification(Guid compteId, string message)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompteId { get; private set; } = compteId;
    public string Message { get; private set; } = message;
    public DateTime DateUtc { get; private set; } = DateTime.UtcNow;
}

internal class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
}

internal class SurRetraitEffectue(NotificationsDbContext db) : IGestionnaireEvenement<RetraitEffectue>
{
    public async Task Traiter(RetraitEffectue e)
    {
        db.Notifications.Add(new Notification(e.CompteId,
            $"Retrait de {e.Montant:0.00} €. Nouveau solde : {e.NouveauSolde:0.00} €."));
        await db.SaveChangesAsync();
    }
}

internal class SurVirementEffectue(NotificationsDbContext db) : IGestionnaireEvenement<VirementEffectue>
{
    public async Task Traiter(VirementEffectue e)
    {
        db.Notifications.AddRange(
            new Notification(e.SourceId, $"Virement de {e.Montant:0.00} € envoyé à {e.TitulaireCible}."),
            new Notification(e.CibleId, $"Virement de {e.Montant:0.00} € reçu de {e.TitulaireSource}."));
        await db.SaveChangesAsync();
    }
}
