using Mk.Contracts;

namespace Mk.Kernel;

public record Result(bool Success, string? Error = null, decimal Fee = 0, decimal Balance = 0, bool NotFound = false)
{
    public static Result Refused(string error) => new(false, error);
    public static Result AccountNotFound(Guid id) => new(false, $"Account {id} not found.", NotFound: true);
}

public record Export(string Content, string MimeType);

/// <summary>
/// The kernel: it keeps the accounts and executes the operations, and nothing else.
/// Everything that varies (checks, fees, statement formats) is delegated to the plugins, at the planned places.
/// </summary>
public sealed class BankingKernel(PluginRegistry plugins, TimeProvider clock, Action<string> log)
{
    private sealed class Account(Guid id, string owner, decimal balance)
    {
        public Guid Id { get; } = id;
        public string Owner { get; } = owner;
        public decimal Balance { get; private set; } = balance;
        public List<StatementLine> Lines { get; } = [];

        public void Movement(decimal amount, string label, DateTime date)
        {
            Balance += amount;
            Lines.Add(new StatementLine(date, label, amount, Balance));
        }
    }

    private readonly Dictionary<Guid, Account> _accounts = [];
    private readonly Lock _lock = new();

    public void Open(Guid id, string owner, decimal initialDeposit)
    {
        lock (_lock)
        {
            var account = new Account(id, owner, 0);
            account.Movement(initialDeposit, "Initial deposit", clock.GetLocalNow().DateTime);
            _accounts[id] = account;
        }
    }

    public AccountStatement? Get(Guid id)
    {
        lock (_lock)
            return _accounts.TryGetValue(id, out var c) ? new AccountStatement(c.Id, c.Owner, c.Balance, c.Lines.ToList()) : null;
    }

    public Result Withdraw(Guid accountId, decimal amount) => Execute(OperationType.Withdrawal, accountId, null, amount);

    public Result Transfer(Guid sourceId, Guid targetId, decimal amount) => Execute(OperationType.Transfer, sourceId, targetId, amount);

    /// <summary>Statement in a format provided by a plugin. Without a format plugin, the kernel cannot export.</summary>
    public Export? Export(Guid accountId, string format, out IReadOnlyList<string> availableFormats)
    {
        var formats = plugins.Active<IStatementFormat>().ToList();
        availableFormats = formats.Select(f => f.Format).ToList();

        var producer = formats.FirstOrDefault(f => f.Format.Equals(format, StringComparison.OrdinalIgnoreCase));
        if (producer is null || Get(accountId) is not { } statement)
            return null;

        string? content = null;
        return Protect(producer, () => content = producer.Produce(statement)) ? new Export(content!, producer.MimeType) : null;
    }

    private Result Execute(OperationType type, Guid accountId, Guid? targetId, decimal amount)
    {
        lock (_lock)
        {
            if (amount <= 0)
                return Result.Refused("The amount must be strictly positive.");
            if (!_accounts.TryGetValue(accountId, out var account))
                return Result.AccountNotFound(accountId);
            Account? target = null;
            if (targetId is { } idTarget && (!_accounts.TryGetValue(idTarget, out target) || idTarget == accountId))
                return target is null ? Result.AccountNotFound(idTarget) : Result.Refused("Cannot transfer to the same account.");

            var date = clock.GetLocalNow().DateTime;
            var operation = new ProposedOperation(type, account.Id, account.Owner, amount, account.Balance, targetId, date);

            // 1. "Check" extension point: a single refusal is enough.
            foreach (var check in plugins.Active<IOperationCheck>())
            {
                string? refusal = null;
                if (!Protect(check, () => refusal = check.Check(operation)))
                    return Result.Refused($"The plugin {check.Name} is faulted: operation refused for safety.");
                if (refusal is not null)
                    return Result.Refused($"[{check.Name}] {refusal}");
            }

            // 2. "Fees" extension point: each rule can add its own.
            var fees = new List<(string Label, decimal Amount)>();
            foreach (var rule in plugins.Active<IFeeRule>())
            {
                decimal feeAmount = 0;
                if (!Protect(rule, () => feeAmount = rule.ComputeFee(operation)))
                    return Result.Refused($"The plugin {rule.Name} is faulted: operation refused for safety.");
                if (feeAmount > 0)
                    fees.Add((rule.Label, feeAmount));
            }

            // 3. The heart of the business, which belongs to the kernel alone.
            var totalFees = fees.Sum(f => f.Amount);
            if (amount + totalFees > account.Balance)
                return Result.Refused($"Insufficient balance (fees included: {totalFees:0.00} €).");

            account.Movement(-amount, target is null ? "Withdrawal" : $"Transfer to {target.Owner}", date);
            foreach (var (label, feeAmount) in fees)
                account.Movement(-feeAmount, label, date);
            target?.Movement(amount, $"Transfer from {account.Owner}", date);

            return new Result(true, Fee: totalFees, Balance: account.Balance);
        }
    }

    /// <summary>
    /// A plugin must never bring the kernel down: its exception is caught, and the plugin is marked "faulted"
    /// (hence disabled) until it is enabled again or reloaded.
    /// </summary>
    private bool Protect(IPlugin plugin, Action call)
    {
        try
        {
            call();
            return true;
        }
        catch (Exception e)
        {
            plugins.ReportError(plugin, e);
            log($"[{plugin.Name}] exception, plugin disabled: {e.Message}");
            return false;
        }
    }
}
