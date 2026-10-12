using Hexa.Hexagon.Ports.Outbound;

namespace Hexa.Tests.Hexagon;

/// <summary>Test double of the INotifications outbound port: records what it is asked to do.</summary>
public class SpyNotifications : INotifications
{
    public List<string> Sent { get; } = [];

    public Task WithdrawalMade(Guid accountId, decimal amount)
    {
        Sent.Add($"withdrawal {amount} {accountId}");
        return Task.CompletedTask;
    }

    public Task TransferMade(Guid sourceId, Guid targetId, decimal amount)
    {
        Sent.Add($"transfer {amount} {sourceId} -> {targetId}");
        return Task.CompletedTask;
    }
}
