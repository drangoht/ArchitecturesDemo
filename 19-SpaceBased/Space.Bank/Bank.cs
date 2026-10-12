using System.Collections.Immutable;
using Space.Grid;

namespace Space.Bank;

/// <summary>An account, as it lives in the grid: immutable, each write replaces it with a new version.</summary>
public sealed record Account(Guid Id, string Owner, decimal Balance, ImmutableList<OperationDto> Operations);

public sealed record OperationDto(DateTimeOffset Date, string Label, decimal Amount);

// ── Requests: the routing key is the account concerned ──
public sealed record OpenAccount(Guid Id, string Owner, decimal InitialDeposit) : IRequest
{
    public string Key => Id.ToString();
}

public sealed record GetAccount(Guid Id) : IRequest
{
    public string Key => Id.ToString();
}

public sealed record Deposit(Guid Id, decimal Amount) : IRequest
{
    public string Key => Id.ToString();
}

public sealed record Withdraw(Guid Id, decimal Amount) : IRequest
{
    public string Key => Id.ToString();
}

/// <summary>Handled by the unit of the source account.</summary>
public sealed record Transfer(Guid Source, Guid Target, decimal Amount) : IRequest
{
    public string Key => Source.ToString();
}

/// <summary>The credit of a transfer, sent by the unit of the source to the unit of the target.</summary>
public sealed record Credit(Guid Id, decimal Amount, string Label) : IInternalRequest
{
    public string Key => Id.ToString();
}

// ── Responses ──
public sealed record AccountDto(Guid Id, string Owner, decimal Balance);
public sealed record TransferDto(Guid Source, Guid Target, decimal Amount, decimal SourceBalance);
public sealed record Refusal(string Reason, bool NotFound = false);

/// <summary>
/// The business code of a processing unit. It sees ONLY its unit's replica: reading an account is a memory access,
/// writing too; the broadcast to the other units and the write to the database happen afterwards, without it.
/// </summary>
public static class Processing
{
    public static async Task<object> Process(object request, UnitContext unit) => request switch
    {
        OpenAccount r => unit.Modify<Account>(r.Key, c =>
            c is not null ? (null, new Refusal("This account already exists."))
            : string.IsNullOrWhiteSpace(r.Owner) || r.InitialDeposit < 0 ? (null, new Refusal("Owner required and initial deposit positive or zero."))
            : New(new Account(r.Id, r.Owner.Trim(), r.InitialDeposit, [new OperationDto(DateTimeOffset.UtcNow, "Initial deposit", r.InitialDeposit)]))),
        GetAccount r => unit.Read<Account>(r.Key) is { } account ? View(account) : NotFound(r.Id),
        Deposit r => Movement(unit, r.Id, r.Amount, debit: false, "Deposit"),
        Credit r => Movement(unit, r.Id, r.Amount, debit: false, r.Label),
        Withdraw r => Movement(unit, r.Id, r.Amount, debit: true, "Withdrawal"),
        Transfer r => await Transfer(unit, r),
        _ => throw new InvalidOperationException($"Unknown request: {request.GetType().Name}.")
    };

    /// <summary>Read, check, write: atomic in the replica of THIS unit only.</summary>
    private static object Movement(UnitContext unit, Guid id, decimal amount, bool debit, string label) =>
        unit.Modify<Account>(id.ToString(), account =>
            account is null ? (null, NotFound(id))
            : amount <= 0 ? (null, new Refusal("The amount must be positive."))
            : debit && amount > account.Balance ? (null, new Refusal($"Insufficient balance ({account.Balance:0.00} €)."))
            : New(account with
            {
                Balance = account.Balance + (debit ? -amount : amount),
                Operations = account.Operations.Add(new OperationDto(DateTimeOffset.UtcNow, label, debit ? -amount : amount))
            }));

    /// <summary>
    /// Debit here, credit by the target's unit (sent through the messaging grid), compensation if the credit fails.
    /// No transaction: the two accounts may belong to two different units.
    /// </summary>
    private static async Task<object> Transfer(UnitContext unit, Transfer r)
    {
        if (r.Source == r.Target)
            return new Refusal("The source and target accounts must be different.");
        // All the data is in each replica: checking the target costs only a memory access.
        if (unit.Read<Account>(r.Target.ToString()) is null)
            return NotFound(r.Target);

        var debit = Movement(unit, r.Source, r.Amount, debit: true, "Transfer sent");
        if (debit is not AccountDto source)
            return debit;

        if (await unit.Send(new Credit(r.Target, r.Amount, "Transfer received")) is Refusal targetRefusal)
        {
            Movement(unit, r.Source, r.Amount, debit: false, "Transfer cancellation");
            return targetRefusal;
        }
        return new TransferDto(r.Source, r.Target, r.Amount, source.Balance);
    }

    private static (Account, object) New(Account account) => (account, View(account));

    private static AccountDto View(Account account) => new(account.Id, account.Owner, account.Balance);

    private static Refusal NotFound(Guid id) => new($"Account {id} not found.", NotFound: true);
}

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>The initial content of the DATABASE: the grid will load it at the first startup, through the data reader.</summary>
    public static IEnumerable<Entry> Accounts() =>
    [
        Entry(new Account(Alice, "Alice", 1_000m, [new OperationDto(DateTimeOffset.UtcNow, "Initial deposit", 1_000m)])),
        Entry(new Account(Bob, "Bob", 500m, [new OperationDto(DateTimeOffset.UtcNow, "Initial deposit", 500m)]))
    ];

    private static Entry Entry(Account account) => new(account.Id.ToString(), account, new Grid.Version(1, "database"));
}
