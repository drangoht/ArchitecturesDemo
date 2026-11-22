using Simple.Data;

namespace Simple.Records;

/// <summary>
/// Active Record (Martin Fowler, 2002): an object that wraps one row of the accounts table,
/// carries the rules that apply to it, and knows how to load and save itself.
/// Data, rules and persistence in the same class: Ruby on Rails and Laravel are built on it.
/// </summary>
public class Account
{
    public const decimal MinimumInitialDeposit = 10m;
    public const decimal TransferLimit = 5_000m;

    private readonly Transaction _tx;
    private readonly List<OperationRow> _pendingOperations = [];
    private bool _isNew;

    private Account(Transaction tx, AccountRow row, bool isNew)
    {
        _tx = tx;
        _isNew = isNew;
        Id = row.Id;
        Owner = row.Owner;
        Balance = row.Balance;
    }

    public Guid Id { get; }
    public string Owner { get; }
    public decimal Balance { get; private set; }

    // ---------- Finding and creating: static methods, on the class itself ----------

    public static Account? Find(Transaction tx, Guid id) =>
        tx.Accounts.Find(id) is { } row ? new Account(tx, row, isNew: false) : null;

    /// <summary>A new account, in memory only: Save() inserts it.</summary>
    public static Account Open(Transaction tx, string owner, decimal initialDeposit)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new RuleViolationException("The owner is required.");
        if (initialDeposit < MinimumInitialDeposit)
            throw new RuleViolationException($"The initial deposit must be at least {MinimumInitialDeposit:0} €.");

        var account = new Account(tx, new AccountRow(Guid.NewGuid(), owner.Trim(), initialDeposit), isNew: true);
        account.Record("Initial deposit", initialDeposit);
        return account;
    }

    // ---------- The rules: written once, next to the data they protect ----------

    public void Withdraw(decimal amount) => Debit(amount, "Withdrawal");

    public void TransferTo(Account target, decimal amount)
    {
        if (target.Id == Id)
            throw new RuleViolationException("Cannot transfer to the same account.");
        if (amount > TransferLimit)
            throw new RuleViolationException($"A transfer cannot exceed {TransferLimit:0} €.");

        Debit(amount, $"Transfer to {target.Owner}");
        target.Balance += amount;
        target.Record($"Transfer from {Owner}", amount);
    }

    private void Debit(decimal amount, string label)
    {
        if (amount <= 0)
            throw new RuleViolationException("The amount must be strictly positive.");
        if (amount > Balance)
            throw new RuleViolationException("Insufficient balance.");
        Balance -= amount;
        Record(label, -amount);
    }

    private void Record(string label, decimal amount) =>
        _pendingOperations.Add(new OperationRow(Guid.NewGuid(), Id, label, amount, _tx.Now));

    // ---------- Persistence: the object writes itself ----------

    /// <summary>INSERT or UPDATE of the row, then INSERT of the operations recorded since the last Save.</summary>
    public void Save()
    {
        var row = new AccountRow(Id, Owner, Balance);
        if (_isNew)
            _tx.Accounts.Insert(row);
        else
            _tx.Accounts.Update(row);
        _isNew = false;

        foreach (var operation in _pendingOperations)
            _tx.Operations.Insert(operation);
        _pendingOperations.Clear();
    }

    public IReadOnlyList<Operation> LatestOperations(int count = 5) => Operation.LatestFor(_tx, Id, count);
}
