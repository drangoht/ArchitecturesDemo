using System.Threading.Channels;

namespace Styles.Bank;

public enum AccountType { Current, Savings }

public sealed record Customer(string Id, string Name, string Email);

public sealed record Operation(string Id, string Number, DateTimeOffset Date, string Label, decimal Amount);

/// <summary>An account, as the bank stores it. Not all its properties are needed by every screen.</summary>
public sealed record Account(
    string Number, string CustomerId, string Label, AccountType Type, string Iban, DateOnly OpeningDate,
    decimal OverdraftLimit, decimal Balance, long Version);

public sealed record Transfer(string Id, string Source, string Target, decimal Amount, string Label, DateTimeOffset Date, decimal SourceBalance);

public enum ErrorCode { UnknownCustomer, UnknownAccount, UnknownTransfer, InvalidParameter, InvalidAmount, SameAccount, InsufficientBalance }

/// <summary>A business error. Each API translates it into its own language: HTTP status, gRPC status, or an "errors" entry.</summary>
public sealed class BusinessError(ErrorCode code, string message) : Exception(message)
{
    public ErrorCode Code => code;
}

/// <summary>What the calls to the "database" cost: enough to see the N+1 hidden behind a single HTTP request.</summary>
public sealed class Statistics
{
    private long _operationReads;

    /// <summary>The number of "database" queries on the operations (a grouped read of several accounts counts as one).</summary>
    public long OperationReads => Interlocked.Read(ref _operationReads);

    internal void CountOperationRead() => Interlocked.Increment(ref _operationReads);
}

/// <summary>
/// The bank, in memory. It offers what the three APIs need, and nothing specific to one of them:
/// single AND grouped reads, a transfer, and a subscription to the new operations of an account.
/// </summary>
public sealed class BankService
{
    private readonly object _lock = new();
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Customer> _customers = [];
    private readonly Dictionary<string, Account> _accounts = [];
    private readonly Dictionary<string, List<Operation>> _operations = [];
    private readonly Dictionary<string, Transfer> _transfers = [];
    private readonly List<(string Number, ChannelWriter<Operation> Writer)> _subscribers = [];
    private long _operationNumber;
    private long _transferNumber;

    public Statistics Statistics { get; } = new();

    public BankService(TimeProvider clock)
    {
        _clock = clock;
        DataSet.Load(this);
    }

    // ─── Reads ───

    public Customer Customer(string id)
    {
        lock (_lock)
            return _customers.GetValueOrDefault(id) ?? throw new BusinessError(ErrorCode.UnknownCustomer, $"Unknown customer \"{id}\".");
    }

    public Account Account(string number)
    {
        lock (_lock)
            return _accounts.GetValueOrDefault(number) ?? throw new BusinessError(ErrorCode.UnknownAccount, $"Unknown account \"{number}\".");
    }

    public IReadOnlyList<Account> AccountsOf(string customerId)
    {
        lock (_lock)
        {
            if (!_customers.ContainsKey(customerId))
                throw new BusinessError(ErrorCode.UnknownCustomer, $"Unknown customer \"{customerId}\".");
            return _accounts.Values.Where(c => c.CustomerId == customerId).OrderBy(c => c.Number).ToList();
        }
    }

    /// <summary>The latest operations of an account, from the most recent to the oldest. One "database" query.</summary>
    public IReadOnlyList<Operation> LatestOperations(string number, int count) =>
        LatestOperations([number], count)[number];

    /// <summary>
    /// The same for several accounts, in ONE "database" query (a "WHERE number IN (…)"): it is what the
    /// GraphQL batched loader calls, and the gRPC service that builds the home screen in one go.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Operation>> LatestOperations(IReadOnlyCollection<string> numbers, int count)
    {
        if (count is < 1 or > 50)
            throw new BusinessError(ErrorCode.InvalidParameter, "The number of operations requested must be between 1 and 50.");
        lock (_lock)
        {
            Statistics.CountOperationRead();
            var result = new Dictionary<string, IReadOnlyList<Operation>>();
            foreach (var number in numbers.Distinct())
            {
                if (!_operations.TryGetValue(number, out var operations))
                    throw new BusinessError(ErrorCode.UnknownAccount, $"Unknown account \"{number}\".");
                result[number] = operations.AsEnumerable().Reverse().Take(count).ToList();
            }
            return result;
        }
    }

    public Transfer Transfer(string id)
    {
        lock (_lock)
            return _transfers.GetValueOrDefault(id) ?? throw new BusinessError(ErrorCode.UnknownTransfer, $"Unknown transfer \"{id}\".");
    }

    // ─── Write ───

    public Transfer MakeTransfer(string source, string target, decimal amount, string? label)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new BusinessError(ErrorCode.InvalidAmount, "The amount must be positive, to the cent.");
        if (source == target)
            throw new BusinessError(ErrorCode.SameAccount, "The source and target accounts must be different.");
        label = string.IsNullOrWhiteSpace(label) ? "Transfer" : label.Trim();

        lock (_lock)
        {
            var sourceAccount = Account(source);
            var targetAccount = Account(target);
            if (sourceAccount.Balance + sourceAccount.OverdraftLimit < amount)
                throw new BusinessError(ErrorCode.InsufficientBalance,
                    $"Insufficient balance: {sourceAccount.Balance:0.00} € available (authorized overdraft: {sourceAccount.OverdraftLimit:0.00} €).");

            // To the second: each API has its precision (ticks in JSON, milliseconds in gRPC, seconds in the GraphQL
            // schema); without this rounding, the same screen differed from one API to the other (seen in a real run).
            var now = _clock.GetUtcNow();
            now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            var id = $"V{++_transferNumber:000}";
            Add(sourceAccount, now, $"{label} to {target}", -amount);
            Add(targetAccount, now, $"{label} from {source}", amount);
            var transfer = new Transfer(id, source, target, amount, label, now, _accounts[source].Balance);
            _transfers[id] = transfer;
            return transfer;
        }
    }

    // ─── Subscription (what the gRPC stream serves) ───

    /// <summary>The operations of the account from now on, as they happen, until the cancellation.</summary>
    public ChannelReader<Operation> Follow(string number, CancellationToken cancellation)
    {
        var channel = Channel.CreateUnbounded<Operation>();
        lock (_lock)
        {
            Account(number);
            _subscribers.Add((number, channel.Writer));
        }
        cancellation.Register(() =>
        {
            lock (_lock)
                _subscribers.RemoveAll(a => a.Writer == channel.Writer);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    public int SubscriberCount
    {
        get { lock (_lock) return _subscribers.Count; }
    }

    // ─── Loading ───

    internal void AddCustomer(Customer customer) => _customers[customer.Id] = customer;

    internal void AddAccount(Account account)
    {
        _accounts[account.Number] = account with { Balance = 0, Version = 0 };
        _operations[account.Number] = [];
    }

    internal void AddOperation(string number, DateTimeOffset date, string label, decimal amount) =>
        Add(_accounts[number], date, label, amount);

    private void Add(Account account, DateTimeOffset date, string label, decimal amount)
    {
        var operation = new Operation($"OP{++_operationNumber:0000}", account.Number, date, label, amount);
        _operations[account.Number].Add(operation);
        _accounts[account.Number] = account with { Balance = account.Balance + amount, Version = account.Version + 1 };
        foreach (var subscriber in _subscribers.Where(a => a.Number == account.Number))
            subscriber.Writer.TryWrite(operation);
    }
}
