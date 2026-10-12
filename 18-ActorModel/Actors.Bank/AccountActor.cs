using Actors.Runtime;

namespace Actors.Bank;

/// <summary>
/// ONE actor per bank account. Its balance is a mere field, modified without lock or transaction:
/// the runtime guarantees that only one message at a time runs here. A thousand simultaneous withdrawals on the same account
/// are therefore handled one after the other, and the balance can never become negative.
/// </summary>
public sealed class AccountActor(StateStore store, Traps traps, TimeProvider clock) : Actor
{
    public const string Type = "account";

    /// <summary>The durable state of an account, as it is stored.</summary>
    public sealed class State
    {
        public string Owner { get; set; } = "";
        public decimal Balance { get; set; }
        public List<Line> Operations { get; set; } = [];
    }

    public sealed record Line(DateTimeOffset Date, string Label, decimal Amount, string? Key);

    /// <summary>null as long as the account has not been opened: a virtual actor "exists" before its account.</summary>
    private State? _state;

    private Guid Id => Guid.Parse(Context.Key);

    public override async Task Start() => _state = await store.Read<State>(Context.Path);

    public override async Task<object?> Receive(object message)
    {
        await traps.Check(Context.Path, Step.Reception);

        return message switch
        {
            OpenAccount m => await Open(m),
            _ when _state is null => new Refusal($"Account {Context.Key} not found.", NotFound: true),
            GetAccount => View(),
            ListOperations => _state.Operations.Select(o => new OperationDto(o.Date, o.Label, o.Amount)).ToList(),
            Deposit m => await Movement(m.Amount, debit: false, "Deposit", key: null),
            Withdraw m => await Movement(m.Amount, debit: true, "Withdrawal", key: null),
            Debit m => await Movement(m.Amount, debit: true, m.Label, m.Key),
            Credit m => await Movement(m.Amount, debit: false, m.Label, m.Key),
            // A message the actor does not understand is a bug of the sender: it crashes, the supervisor decides.
            _ => throw new InvalidOperationException($"Unexpected message: {message.GetType().Name}.")
        };
    }

    private async Task<object> Open(OpenAccount m)
    {
        if (_state is not null)
            return new Refusal($"Account {Context.Key} is already open.");
        if (string.IsNullOrWhiteSpace(m.Owner) || m.InitialDeposit < 0)
            return new Refusal("Owner required and initial deposit positive or zero.");

        var state = new State { Owner = m.Owner.Trim(), Balance = m.InitialDeposit };
        state.Operations.Add(new Line(clock.GetUtcNow(), "Initial deposit", m.InitialDeposit, null));
        await store.Write(Context.Path, state);
        _state = state;
        return View();
    }

    /// <summary>A credit or a debit. Read, check, modify: no risk, nobody else is here.</summary>
    private async Task<object> Movement(decimal amount, bool debit, string label, string? key)
    {
        var state = _state!;
        // A replayed transfer (after a failure, a timeout…) sends the same key back: already applied, nothing to do.
        if (key is not null && state.Operations.Any(o => o.Key == key))
            return View();
        if (amount <= 0)
            return new Refusal("The amount must be positive.");
        if (debit && amount > state.Balance)
            return new Refusal($"Insufficient balance ({state.Balance:0.00} €).");

        var signed = debit ? -amount : amount;
        state.Balance += signed;
        state.Operations.Add(new Line(clock.GetUtcNow(), label, signed, key));

        // A failure here leaves the state modified IN MEMORY but not in the storage. It does not matter: the supervisor throws
        // this incarnation away, the next one reloads the last written state. The withdrawal simply did not happen.
        await traps.Check(Context.Path, Step.BeforeSave);
        await store.Write(Context.Path, state);
        return View();
    }

    private AccountDto View() => new(Id, _state!.Owner, _state.Balance);
}
