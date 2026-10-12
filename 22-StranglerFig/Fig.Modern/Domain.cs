namespace Fig.Modern;

/// <summary>Assembly marker, for the tests (each project of the demo has its own Program).</summary>
public sealed class Marker;

public enum Origin { Legacy, Modern }

/// <summary>An operation, in the modern service's model: euros as decimal, date with its time zone.</summary>
public sealed record Operation(string Id, DateTimeOffset Date, string Label, decimal Amount, Origin Origin);

public sealed class Account(string number, string owner)
{
    private readonly List<Operation> _operations = [];

    public string Number { get; } = number;
    public string Owner { get; } = owner;
    public decimal Balance { get; private set; }

    /// <summary>In recording order.</summary>
    public IReadOnlyList<Operation> Operations => _operations;

    internal bool Record(Operation operation)
    {
        if (_operations.Any(o => o.Id == operation.Id))
            return false;   // already known: a replayed batch, or the echo of an operation written here, does not count twice
        _operations.Add(operation);
        Balance += operation.Amount;
        return true;
    }
}

public sealed class BusinessError(int status, string message) : Exception(message)
{
    public int Status => status;
}

// What the anti-corruption layer extracts from the legacy log, in the modern service's vocabulary.
public abstract record LegacyFact;
public sealed record AccountOpened(string Number, string Owner) : LegacyFact;
public sealed record OperationRecorded(string Number, Operation Operation) : LegacyFact;

/// <summary>An operation written here, to forward to the legacy system (outbox, filled under the same lock as the write).</summary>
public sealed record OperationToForward(string Number, Operation Operation);

public sealed record TransferRequest(string Source, string Target, decimal Amount);
public sealed record TransferResult(string Reference, decimal SourceBalance);

/// <summary>The modern service's data, with the position reached in the legacy log and the outbox.</summary>
public sealed class Store
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Account> _accounts = [];
    private readonly List<OperationToForward> _toForward = [];
    private long _legacyPosition;
    private long _transferNumber;

    public long LegacyPosition
    {
        get { lock (_lock) return _legacyPosition; }
    }

    public T Read<T>(string number, Func<Account, T> read)
    {
        lock (_lock)
            return read(_accounts.GetValueOrDefault(number) ?? throw new BusinessError(404, "UNKNOWN ACCOUNT"));
    }

    /// <summary>Applies facts coming from the legacy system AND moves the position forward, together: a batch is applied entirely or not at all.</summary>
    public int ApplyFromLegacy(IReadOnlyList<LegacyFact> facts, long position)
    {
        lock (_lock)
        {
            var applied = 0;
            foreach (var fact in facts)
            {
                switch (fact)
                {
                    case AccountOpened o when !_accounts.ContainsKey(o.Number):
                        _accounts[o.Number] = new Account(o.Number, o.Owner);
                        applied++;
                        break;
                    case OperationRecorded e when _accounts[e.Number].Record(e.Operation):
                        applied++;
                        break;
                }
            }
            _legacyPosition = Math.Max(_legacyPosition, position);
            return applied;
        }
    }

    /// <summary>
    /// The transfer, rewritten in the modern service. The rules and the error messages are those of the legacy system: they are
    /// part of the contract. The two operations and their forwarding to the legacy system are recorded together (outbox).
    /// </summary>
    public TransferResult Transfer(string source, string target, decimal amount, DateTimeOffset now)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new BusinessError(400, "INVALID AMOUNT");
        if (source == target)
            throw new BusinessError(400, "IDENTICAL ACCOUNTS");
        lock (_lock)
        {
            var debited = _accounts.GetValueOrDefault(source) ?? throw new BusinessError(404, "UNKNOWN ACCOUNT");
            var credited = _accounts.GetValueOrDefault(target) ?? throw new BusinessError(404, "UNKNOWN ACCOUNT");
            if (debited.Balance < amount)
                throw new BusinessError(400, "INSUFFICIENT BALANCE");
            var reference = $"TRF-N{++_transferNumber:D5}";
            var debit = new Operation($"M-{Guid.NewGuid():N}", now, $"TRF TO {target}", -amount, Origin.Modern);
            var credit = new Operation($"M-{Guid.NewGuid():N}", now, $"TRF FROM {source}", amount, Origin.Modern);
            debited.Record(debit);
            credited.Record(credit);
            _toForward.Add(new OperationToForward(source, debit));
            _toForward.Add(new OperationToForward(target, credit));
            return new TransferResult(reference, debited.Balance);
        }
    }

    public IReadOnlyList<OperationToForward> ToForward()
    {
        lock (_lock)
            return _toForward.ToList();
    }

    public void MarkForwarded(IEnumerable<OperationToForward> forwarded)
    {
        lock (_lock)
            foreach (var f in forwarded)
                _toForward.Remove(f);
    }

    public IReadOnlyList<string> Numbers()
    {
        lock (_lock)
            return _accounts.Keys.Order().ToList();
    }
}
