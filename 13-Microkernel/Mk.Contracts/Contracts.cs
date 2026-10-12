namespace Mk.Contracts;

/// <summary>
/// Every plugin implements IPlugin, plus one or more extension points (IOperationCheck, IFeeRule, IStatementFormat).
/// The kernel discovers these classes by reflection in the DLLs of the "plugins" folder.
/// </summary>
public interface IPlugin
{
    string Name { get; }
    string Description { get; }

    /// <summary>Minimum version of the kernel the plugin needs. A plugin that is too recent is refused at load time.</summary>
    Version MinimumKernelVersion => new(1, 0);

    /// <summary>Called once at load time, with the settings read from the plugin's plugin.json.</summary>
    void Initialize(PluginContext context) { }
}

/// <summary>What the kernel makes available to a plugin. The plugin sees NOTHING else of the kernel.</summary>
public sealed class PluginContext(IReadOnlyDictionary<string, string> settings, Action<string> log)
{
    public string? Setting(string key) => settings.GetValueOrDefault(key);
    public void Log(string message) => log(message);
}

// ─── The exchanged data: mere values, never the kernel's internal objects ───

public enum OperationType { Withdrawal, Transfer }

public record ProposedOperation(
    OperationType Type, Guid AccountId, string Owner, decimal Amount, decimal Balance, Guid? TargetAccount, DateTime Date);

public record StatementLine(DateTime Date, string Label, decimal Amount, decimal BalanceAfter);

public record AccountStatement(Guid AccountId, string Owner, decimal Balance, IReadOnlyList<StatementLine> Lines);

// ─── The extension points ───

/// <summary>Can refuse an operation before it takes place.</summary>
public interface IOperationCheck : IPlugin
{
    /// <returns>null if the operation is allowed, otherwise the reason for the refusal.</returns>
    string? Check(ProposedOperation operation);
}

/// <summary>Adds fees to an operation.</summary>
public interface IFeeRule : IPlugin
{
    string Label { get; }
    decimal ComputeFee(ProposedOperation operation);
}

/// <summary>Produces an account statement in a given format.</summary>
public interface IStatementFormat : IPlugin
{
    string Format { get; }
    string MimeType { get; }
    string Produce(AccountStatement statement);
}
