using Mk.Contracts;

namespace Mk.Plugins.Blocklist;

/// <summary>"Check" extension point: forbids transfers to the accounts listed in plugin.json.</summary>
public sealed class Blocklist : IOperationCheck
{
    private HashSet<Guid> _blockedAccounts = [];

    public string Name => "Blocklist";
    public string Description => "Refuses transfers to the accounts of the blocklist.";

    public void Initialize(PluginContext context)
    {
        _blockedAccounts = (context.Setting("blockedAccounts") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Guid.Parse)
            .ToHashSet();
        context.Log($"{_blockedAccounts.Count} account(s) on the blocklist");
    }

    public string? Check(ProposedOperation operation) =>
        operation.Type == OperationType.Transfer && operation.TargetAccount is { } target && _blockedAccounts.Contains(target)
            ? "Transfers to this account are forbidden (blocklist)."
            : null;
}
