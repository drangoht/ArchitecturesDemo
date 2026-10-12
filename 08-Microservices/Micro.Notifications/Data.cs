using Microsoft.EntityFrameworkCore;

namespace Micro.Notifications;

/// <summary>Marker type of the service (used by the tests to start THIS service).</summary>
public sealed class Marker;

// Received messages: LOCAL copies of the JSON contracts published by Accounts and Transfers.
public record WithdrawalMade(Guid AccountId, decimal Amount, decimal NewBalance);
public record TransferMade(Guid TransferId, Guid SourceId, string SourceOwner, Guid TargetId, string TargetOwner, decimal Amount);

public class Notification(Guid accountId, string message)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AccountId { get; private set; } = accountId;
    public string Message { get; private set; } = message;
    public DateTime DateUtc { get; private set; } = DateTime.UtcNow;
}

public class ProcessedMessage
{
    public Guid MessageId { get; set; }
}

public class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ProcessedMessage>().HasKey(m => m.MessageId);
}
