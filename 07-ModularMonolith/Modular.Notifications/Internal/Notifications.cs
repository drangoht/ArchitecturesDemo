using Microsoft.EntityFrameworkCore;
using Modular.Accounts.Contracts;
using Modular.Shared;
using Modular.Transfers.Contracts;

namespace Modular.Notifications;

internal class Notification(Guid accountId, string message)
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid AccountId { get; private set; } = accountId;
    public string Message { get; private set; } = message;
    public DateTime DateUtc { get; private set; } = DateTime.UtcNow;
}

internal class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
}

internal class OnWithdrawalMade(NotificationsDbContext db) : IEventHandler<WithdrawalMade>
{
    public async Task Handle(WithdrawalMade e)
    {
        db.Notifications.Add(new Notification(e.AccountId,
            $"Withdrawal of {e.Amount:0.00} €. New balance: {e.NewBalance:0.00} €."));
        await db.SaveChangesAsync();
    }
}

internal class OnTransferMade(NotificationsDbContext db) : IEventHandler<TransferMade>
{
    public async Task Handle(TransferMade e)
    {
        db.Notifications.AddRange(
            new Notification(e.SourceId, $"Transfer of {e.Amount:0.00} € sent to {e.TargetOwner}."),
            new Notification(e.TargetId, $"Transfer of {e.Amount:0.00} € received from {e.SourceOwner}."));
        await db.SaveChangesAsync();
    }
}
