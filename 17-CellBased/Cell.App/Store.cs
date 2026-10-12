namespace Cell.App;

/// <summary>Marker of the assembly, for the tests.</summary>
public sealed class Marker;

public sealed class BusinessRuleException(string message) : Exception(message);

public sealed class NotFoundException(string message) : Exception(message);

// Exchange format of a complete customer: creation, migration from one cell to another.
public sealed record AccountExport(Guid Id, string Label, decimal Balance);
public sealed record MovementExport(Guid AccountId, DateTimeOffset Date, string Label, decimal Amount);
public sealed record CustomerExport(string Id, string Name, List<AccountExport> Accounts, List<MovementExport> Movements, List<Guid> ProcessedOperations);

/// <summary>
/// The data OF THIS CELL: only the customers assigned to it. In production, each cell has its own
/// database (its own instance, sometimes its own cloud account): a database that goes down only affects one cell.
/// </summary>
public sealed class Store
{
    private sealed class Customer(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public Dictionary<Guid, (string Label, decimal Balance)> Accounts { get; } = [];
        public List<MovementExport> Movements { get; } = [];

        /// <summary>The operation identifiers already applied (idempotence): they follow the customer if it changes cell.</summary>
        public HashSet<Guid> ProcessedOperations { get; } = [];
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Customer> _customers = new(StringComparer.OrdinalIgnoreCase);

    public int CustomerCount
    {
        get { lock (_lock) return _customers.Count; }
    }

    public bool Hosts(string customer)
    {
        lock (_lock) return _customers.ContainsKey(customer);
    }

    public void Import(CustomerExport export)
    {
        lock (_lock)
        {
            if (_customers.ContainsKey(export.Id))
                throw new BusinessRuleException($"Customer \"{export.Id}\" is already in this cell.");
            var customer = new Customer(export.Id, export.Name);
            foreach (var c in export.Accounts)
                customer.Accounts[c.Id] = (c.Label, c.Balance);
            customer.Movements.AddRange(export.Movements);
            customer.ProcessedOperations.UnionWith(export.ProcessedOperations);
            _customers[export.Id] = customer;
        }
    }

    public CustomerExport Export(string customerId)
    {
        lock (_lock)
        {
            var c = Find(customerId);
            return new CustomerExport(c.Id, c.Name,
                c.Accounts.Select(x => new AccountExport(x.Key, x.Value.Label, x.Value.Balance)).ToList(),
                [.. c.Movements], [.. c.ProcessedOperations]);
        }
    }

    public void Delete(string customerId)
    {
        lock (_lock)
            _customers.Remove(customerId);
    }

    public IReadOnlyList<AccountExport> Accounts(string customerId)
    {
        lock (_lock)
            return Find(customerId).Accounts.Select(x => new AccountExport(x.Key, x.Value.Label, x.Value.Balance)).ToList();
    }

    public IReadOnlyList<MovementExport> Movements(string customerId, Guid account)
    {
        lock (_lock)
            return Find(customerId).Movements.Where(m => m.AccountId == account).OrderByDescending(m => m.Date).ToList();
    }

    /// <summary>Changes a balance. <paramref name="compute"/> gives the new balance: that is where the version of the code comes in.</summary>
    public decimal Move(string customerId, Guid account, Func<decimal, decimal> compute, string label, DateTimeOffset date, Guid? operationId = null)
    {
        lock (_lock)
        {
            var c = Find(customerId);
            if (!c.Accounts.TryGetValue(account, out var current))
                throw new NotFoundException($"Account {account} not found at {customerId}.");
            if (operationId is { } id && !c.ProcessedOperations.Add(id))
                return current.Balance;   // already applied: a retry does not credit twice
            var updated = compute(current.Balance);
            if (updated < 0)
            {
                if (operationId is { } cancelled)
                    c.ProcessedOperations.Remove(cancelled);
                throw new BusinessRuleException($"Insufficient balance ({current.Balance:0.00} €).");
            }
            c.Accounts[account] = (current.Label, updated);
            c.Movements.Add(new MovementExport(account, date, label, updated - current.Balance));
            return updated;
        }
    }

    /// <summary>Transfer between two customers of THE SAME cell: a single local transaction, as in a monolith.</summary>
    public decimal TransferLocally(string source, Guid sourceAccount, string target, Guid targetAccount, decimal amount, DateTimeOffset date)
    {
        lock (_lock)
        {
            var s = Find(source);
            var c = Find(target);
            if (!s.Accounts.TryGetValue(sourceAccount, out var debit) || !c.Accounts.TryGetValue(targetAccount, out var credit))
                throw new NotFoundException("Account not found.");
            if (debit.Balance < amount)
                throw new BusinessRuleException($"Insufficient balance ({debit.Balance:0.00} €).");
            s.Accounts[sourceAccount] = (debit.Label, debit.Balance - amount);
            c.Accounts[targetAccount] = (credit.Label, credit.Balance + amount);
            s.Movements.Add(new MovementExport(sourceAccount, date, $"Transfer to {c.Name}", -amount));
            c.Movements.Add(new MovementExport(targetAccount, date, $"Transfer from {s.Name}", amount));
            return debit.Balance - amount;
        }
    }

    private Customer Find(string customerId) =>
        _customers.GetValueOrDefault(customerId) ?? throw new NotFoundException($"Customer \"{customerId}\" is not in this cell.");
}
