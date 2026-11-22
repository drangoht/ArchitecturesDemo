using Med.Bank.Data;
using Med.Bank.Domain;
using Med.Mediator;

namespace Med.Bank.Features.Transfers;

/// <summary>
/// Announced by the transfer, handled by anyone interested. In process and synchronous: the handlers run
/// before the transfer's SaveChanges, in the same unit of work, so their writes are saved with it, or not at all.
/// </summary>
public record TransferCompleted(Guid SourceId, string SourceOwner, Guid TargetId, decimal Amount) : INotification;

internal class NotifyRecipient(BankDb db) : INotificationHandler<TransferCompleted>
{
    public Task Handle(TransferCompleted notification, CancellationToken ct)
    {
        db.Notifications.Add(new Notification
        {
            AccountId = notification.TargetId,
            Message = $"You received {notification.Amount:0.##} € from {notification.SourceOwner}."
        });
        return Task.CompletedTask;
    }
}

internal class ReviewLargeTransfer(BankDb db) : INotificationHandler<TransferCompleted>
{
    public const decimal Threshold = 1_000m;

    public Task Handle(TransferCompleted notification, CancellationToken ct)
    {
        if (notification.Amount >= Threshold)
            db.ComplianceAlerts.Add(new ComplianceAlert
            {
                Reason = $"Transfer of {notification.Amount:0.##} € from {notification.SourceOwner} to review."
            });
        return Task.CompletedTask;
    }
}
