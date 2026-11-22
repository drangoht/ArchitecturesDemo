using Simple.Data;

namespace Simple.Scripts;

/// <summary>
/// Transaction Script (Martin Fowler, 2002): one procedure per use case, read from top to bottom.
/// Each script opens its transaction, reads the rows, checks the rules, writes the rows and commits.
/// There is no object model: the rows are plain data and the rules live in the scripts.
/// </summary>
public class BankScripts(Database database)
{
    public const decimal MinimumInitialDeposit = 10m;
    public const decimal TransferLimit = 5_000m;

    public Guid OpenAccount(string owner, decimal initialDeposit)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new ScriptRefusedException("The owner is required.");
        if (initialDeposit < MinimumInitialDeposit)
            throw new ScriptRefusedException($"The initial deposit must be at least {MinimumInitialDeposit:0} €.");

        using var tx = database.Begin();
        var id = Guid.NewGuid();
        tx.Accounts.Insert(new AccountRow(id, owner.Trim(), initialDeposit));
        tx.Operations.Insert(new OperationRow(Guid.NewGuid(), id, "Initial deposit", initialDeposit, tx.Now));
        tx.Commit();
        return id;
    }

    public AccountView GetAccount(Guid id)
    {
        using var tx = database.Begin();
        var account = tx.Accounts.Find(id) ?? throw new AccountNotFoundException(id);
        var operations = tx.Operations.Where(o => o.AccountId == id)
            .OrderByDescending(o => o.DateUtc)
            .Take(5)
            .Select(o => new OperationView(o.Label, o.Amount, o.DateUtc))
            .ToList();
        return new AccountView(account.Id, account.Owner, account.Balance, operations);
    }

    public WithdrawalResult Withdraw(Guid id, decimal amount)
    {
        using var tx = database.Begin();
        var account = tx.Accounts.Find(id) ?? throw new AccountNotFoundException(id);

        // The same two checks are copied in Transfer: each script carries its own copy of the rules.
        if (amount <= 0)
            throw new ScriptRefusedException("The amount must be strictly positive.");
        if (amount > account.Balance)
            throw new ScriptRefusedException("Insufficient balance.");

        var newBalance = account.Balance - amount;
        tx.Accounts.Update(account with { Balance = newBalance });
        tx.Operations.Insert(new OperationRow(Guid.NewGuid(), id, "Withdrawal", -amount, tx.Now));
        tx.Commit();
        return new WithdrawalResult(id, newBalance);
    }

    public TransferResult Transfer(Guid sourceId, Guid targetId, decimal amount)
    {
        if (sourceId == targetId)
            throw new ScriptRefusedException("Cannot transfer to the same account.");
        if (amount > TransferLimit)
            throw new ScriptRefusedException($"A transfer cannot exceed {TransferLimit:0} €.");

        using var tx = database.Begin();
        var source = tx.Accounts.Find(sourceId) ?? throw new AccountNotFoundException(sourceId);
        if (amount <= 0)
            throw new ScriptRefusedException("The amount must be strictly positive.");
        if (amount > source.Balance)
            throw new ScriptRefusedException("Insufficient balance.");
        var newSource = source with { Balance = source.Balance - amount };
        tx.Accounts.Update(newSource);

        // The target is read after the debit. If it does not exist, the exception leaves the script
        // without Commit: the transaction throws the debit away.
        var target = tx.Accounts.Find(targetId) ?? throw new AccountNotFoundException(targetId);
        var newTarget = target with { Balance = target.Balance + amount };
        tx.Accounts.Update(newTarget);

        tx.Operations.Insert(new OperationRow(Guid.NewGuid(), sourceId, $"Transfer to {target.Owner}", -amount, tx.Now));
        tx.Operations.Insert(new OperationRow(Guid.NewGuid(), targetId, $"Transfer from {source.Owner}", amount, tx.Now));
        tx.Commit();
        return new TransferResult(newSource.Balance, newTarget.Balance);
    }
}
