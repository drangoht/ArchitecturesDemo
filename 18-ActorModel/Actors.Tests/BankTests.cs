using Actors.Bank;
using Actors.Runtime;

namespace Actors.Tests;

/// <summary>The business code as actors, without HTTP: concurrency, failures, resumption, passivation.</summary>
public class BankTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid();

    private readonly ManualClock _clock = new();
    private readonly StateStore _store = new();
    private readonly Traps _traps = new();
    private readonly ActorSystem _system;

    public BankTests()
    {
        _system = new ActorSystem(_clock).InstallBank(_store, _traps, _clock);
        _system.Account(Alice).Ask(new OpenAccount("Alice", 1_000m)).Wait();
        _system.Account(Bob).Ask(new OpenAccount("Bob", 500m)).Wait();
    }

    private async Task<decimal> Balance(Guid account) =>
        ((AccountDto)(await _system.Account(account).Ask(new GetAccount()))!).Balance;

    private async Task<List<OperationDto>> Operations(Guid account) =>
        (List<OperationDto>)(await _system.Account(account).Ask(new ListOperations()))!;

    private Task<object?> Transfer(string id, Guid source, Guid target, decimal amount) =>
        _system.Transfer(id).Ask(new ExecuteTransfer(source, target, amount));

    [Fact]
    public async Task A_thousand_simultaneous_withdrawals_never_make_the_balance_negative()
    {
        var account = _system.Account(Alice);

        var responses = await Task.WhenAll(Enumerable.Range(0, 1_000).Select(_ => Task.Run(() => account.Ask(new Withdraw(3m)))));

        var accepted = responses.OfType<AccountDto>().ToList();
        Assert.Equal(333, accepted.Count);            // 333 × 3 € = 999 €, the 334th no longer goes through
        Assert.Equal(667, responses.OfType<Refusal>().Count());
        Assert.Equal(1m, await Balance(Alice));
        Assert.All(accepted, c => Assert.True(c.Balance >= 0));
        Assert.Equal(334, (await Operations(Alice)).Count);  // initial deposit + 333 withdrawals, none twice
    }

    [Fact]
    public async Task Simultaneous_crossed_transfers_do_not_deadlock()
    {
        var transfers = Enumerable.Range(0, 200).SelectMany(i => new[]
        {
            Task.Run(() => Transfer($"ab-{i}", Alice, Bob, 10m)),
            Task.Run(() => Transfer($"ba-{i}", Bob, Alice, 10m))
        });

        var responses = await Task.WhenAll(transfers).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(responses, r => Assert.Contains(((TransferDto)r!).Status, new[] { TransferStatus.Completed, TransferStatus.Refused }));
        Assert.Equal(1_500m, await Balance(Alice) + await Balance(Bob));  // money is neither created nor lost
    }

    [Fact]
    public async Task A_failure_before_saving_cancels_the_withdrawal_in_progress()
    {
        _traps.Arm(new Trap($"account/{Alice}", Step.BeforeSave));

        await Assert.ThrowsAsync<ActorFailedException>(() => _system.Account(Alice).Ask(new Withdraw(100m)));

        // The incarnation that had already withdrawn 100 € in memory was thrown away; the new one read the storage again.
        Assert.Equal(1_000m, await Balance(Alice));
        Assert.Single(await Operations(Alice));
        Assert.Equal(1, _system.State().Restarts);
        Assert.Equal(900m, ((AccountDto)(await _system.Account(Alice).Ask(new Withdraw(100m)))!).Balance);  // and the account works
    }

    [Fact]
    public async Task A_transfer_interrupted_after_the_debit_completes_when_replayed()
    {
        _traps.Arm(new Trap("transfer/v1", Step.AfterDebit));

        await Assert.ThrowsAsync<ActorFailedException>(() => Transfer("v1", Alice, Bob, 100m));
        Assert.Equal((900m, 500m), (await Balance(Alice), await Balance(Bob)));  // the money is "in flight"
        Assert.Equal(TransferStatus.DebitDone, ((TransferDto)(await _system.Transfer("v1").Ask(new GetTransfer()))!).Status);

        var replayed = (TransferDto)(await Transfer("v1", Alice, Bob, 100m))!;

        Assert.Equal(TransferStatus.Completed, replayed.Status);
        Assert.Equal((900m, 600m), (await Balance(Alice), await Balance(Bob)));  // debited only once
    }

    [Fact]
    public async Task An_account_failure_propagates_to_the_transfer_which_can_be_replayed()
    {
        _traps.Arm(new Trap($"account/{Bob}", Step.Reception));

        var failure = await Assert.ThrowsAsync<ActorFailedException>(() => Transfer("v2", Alice, Bob, 100m));

        // The account crashed, so the transfer's request failed, so the transfer crashed in turn.
        Assert.Equal("transfer/v2", failure.Path);
        Assert.Equal(TransferStatus.Completed, ((TransferDto)(await Transfer("v2", Alice, Bob, 100m))!).Status);
        Assert.Equal((900m, 600m), (await Balance(Alice), await Balance(Bob)));
        Assert.Equal(2, _system.State().Restarts);  // Bob's account, then the transfer
    }

    [Fact]
    public async Task A_debit_replayed_with_the_same_key_is_applied_only_once()
    {
        var account = _system.Account(Alice);

        await account.Ask(new Debit("key-1", 100m, "Transfer"));
        await account.Ask(new Debit("key-1", 100m, "Transfer"));

        Assert.Equal(900m, await Balance(Alice));
    }

    [Fact]
    public async Task A_transfer_to_an_account_that_does_not_exist_is_compensated()
    {
        var transfer = (TransferDto)(await Transfer("v3", Alice, Guid.NewGuid(), 100m))!;

        Assert.Equal(TransferStatus.Cancelled, transfer.Status);
        Assert.Contains("not found", transfer.Reason);
        Assert.Equal(1_000m, await Balance(Alice));
        Assert.Equal([1_000m, -100m, 100m], (await Operations(Alice)).Select(o => o.Amount));  // debit then cancellation, recorded
    }

    [Fact]
    public async Task Replaying_a_completed_transfer_returns_the_same_result_without_effect()
    {
        var first = await Transfer("v4", Alice, Bob, 100m);
        var second = await Transfer("v4", Alice, Bob, 100m);

        Assert.Equal(first, second);
        Assert.Equal(900m, await Balance(Alice));
        Assert.IsType<Refusal>(await Transfer("v4", Alice, Bob, 999m));  // same identifier, another transfer: refused
    }

    [Fact]
    public async Task Replaying_a_refused_transfer_never_credits_the_target()
    {
        Assert.Equal(TransferStatus.Refused, ((TransferDto)(await Transfer("v6", Bob, Alice, 600m))!).Status);  // Bob only has 500 €
        await _system.Account(Bob).Ask(new Deposit(200m));

        // The refusal is final: replaying does not try the debit again, and above all does not credit Alice without debiting Bob.
        Assert.Equal(TransferStatus.Refused, ((TransferDto)(await Transfer("v6", Bob, Alice, 600m))!).Status);
        Assert.Equal((1_000m, 700m), (await Balance(Alice), await Balance(Bob)));
    }

    [Fact]
    public async Task Business_refusals_are_responses_not_failures()
    {
        Assert.IsType<Refusal>(await _system.Account(Alice).Ask(new Withdraw(5_000m)));
        Assert.IsType<Refusal>(await _system.Account(Alice).Ask(new Deposit(-5m)));
        Assert.IsType<Refusal>(await _system.Account(Alice).Ask(new OpenAccount("Alice", 0m)));
        Assert.Equal(TransferStatus.Refused, ((TransferDto)(await Transfer("v5", Bob, Alice, 600m))!).Status);
        Assert.True(((Refusal)(await _system.Account(Guid.NewGuid()).Ask(new GetAccount()))!).NotFound);

        Assert.Equal(0, _system.State().Restarts);
    }

    [Fact]
    public async Task A_passivated_account_finds_its_balance_again_in_the_storage()
    {
        await _system.Account(Alice).Ask(new Withdraw(100m));
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(2, _system.Passivate(TimeSpan.FromMinutes(1)));
        Assert.Equal(0, _system.State().Active);
        Assert.Contains($"account/{Alice}", _store.Paths());

        Assert.Equal(900m, await Balance(Alice));
        Assert.Equal(["account/" + Alice], _system.State().Actors.Select(a => a.Path));  // only Alice was woken up
    }
}
