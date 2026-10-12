using Actors.Runtime;

namespace Actors.Bank;

/// <summary>
/// ONE actor per transfer, identified by the transfer identifier. There is no transaction between two accounts
/// (two actors, two separate states): the transfer converses with them, step by step, and records where it stands.
/// <list type="bullet">
/// <item>Its identity makes the idempotence: replaying "transfer/v-42" wakes up the SAME actor, which already knows the answer.</item>
/// <item>No deadlock is possible: the accounts never ask anyone anything, only the transfer waits for
/// their answers. With locks, simultaneous A→B and B→A would require locking in a fixed order.</item>
/// </list>
/// </summary>
public sealed class TransferActor(StateStore store, Traps traps) : Actor
{
    public const string Type = "transfer";

    public sealed class State
    {
        public Guid Source { get; set; }
        public Guid Target { get; set; }
        public decimal Amount { get; set; }
        public TransferStatus Status { get; set; }
        public string? Reason { get; set; }
    }

    private State? _state;

    public override async Task Start() => _state = await store.Read<State>(Context.Path);

    public override async Task<object?> Receive(object message) => message switch
    {
        ExecuteTransfer m => await Execute(m),
        GetTransfer => _state is null ? new Refusal($"Transfer {Context.Key} not found.", NotFound: true) : View(),
        _ => throw new InvalidOperationException($"Unexpected message: {message.GetType().Name}.")
    };

    private async Task<object> Execute(ExecuteTransfer m)
    {
        if (_state is not null && (_state.Source, _state.Target, _state.Amount) != (m.Source, m.Target, m.Amount))
            return new Refusal($"Identifier {Context.Key} already designates another transfer.");
        if (_state is { Status: TransferStatus.Completed or TransferStatus.Refused or TransferStatus.Cancelled })
            return View();  // already finished: same response, no effect
        if (m.Source == m.Target)
            return new Refusal("The source and target accounts must be different.");
        if (m.Amount <= 0)
            return new Refusal("The amount must be positive.");

        if (_state is null)
        {
            _state = new State { Source = m.Source, Target = m.Target, Amount = m.Amount, Status = TransferStatus.InProgress };
            await Save();
        }

        var source = Context.Actor(AccountActor.Type, m.Source.ToString());
        var target = Context.Actor(AccountActor.Type, m.Target.ToString());
        var key = $"transfer-{Context.Key}";

        // ① Debit the source. If a previous incarnation crashed after this step, it is not done again
        //    (and even if done again, the key would make it harmless on the account side).
        if (_state.Status == TransferStatus.InProgress)
        {
            if (await source.Ask(new Debit(key, m.Amount, $"Transfer {Context.Key} sent")) is Refusal refusal)
                return await Finish(TransferStatus.Refused, refusal.Reason);
            _state.Status = TransferStatus.DebitDone;
            await Save();
        }

        await traps.Check(Context.Path, Step.AfterDebit);

        // ② Credit the target; if it refuses (account that does not exist…), ③ give the money back to the source: a compensation,
        //    as in the saga of demo 14, but here each participant is an actor.
        if (await target.Ask(new Credit(key, m.Amount, $"Transfer {Context.Key} received")) is Refusal targetRefusal)
        {
            await source.Ask(new Credit($"{key}-cancellation", m.Amount, $"Cancellation of transfer {Context.Key}"));
            return await Finish(TransferStatus.Cancelled, targetRefusal.Reason);
        }
        return await Finish(TransferStatus.Completed, null);
    }

    private async Task<object> Finish(TransferStatus status, string? reason)
    {
        _state!.Status = status;
        _state.Reason = reason;
        await Save();
        return View();
    }

    private Task Save() => store.Write(Context.Path, _state!);

    private TransferDto View() => new(Context.Key, _state!.Source, _state.Target, _state.Amount, _state.Status, _state.Reason);
}
