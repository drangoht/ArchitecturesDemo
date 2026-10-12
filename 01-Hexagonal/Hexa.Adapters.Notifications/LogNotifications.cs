using Hexa.Hexagon.Ports.Outbound;

namespace Hexa.Adapters.Notifications;

/// <summary>Outbound adapter: writes the notifications to a text stream (the console, a file…).</summary>
public class LogNotifications(TextWriter output) : INotifications
{
    public Task WithdrawalMade(Guid accountId, decimal amount) =>
        output.WriteLineAsync($"[notification] Withdrawal of {amount:0.00} € from account {accountId}");

    public Task TransferMade(Guid sourceId, Guid targetId, decimal amount) =>
        output.WriteLineAsync($"[notification] Transfer of {amount:0.00} € from {sourceId} to {targetId}");
}
