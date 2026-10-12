using Saga.Orchestrator;
using Saga.Participants;

namespace Saga.Tests;

/// <summary>A clock moved forward by hand: the retry and deadline tests are instant and deterministic.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

public class SagaEngineTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Carla = Guid.NewGuid();

    private readonly Chaos _chaos = new();
    private readonly AccountsService _accounts = new();
    private readonly NotificationsService _notifications;
    private readonly SagaStore _store = new();
    private readonly ManualClock _clock = new();
    private readonly SagaOptions _options = new() { PauseBetweenStepsMs = 0, InitialDelayMs = 100, ComplianceDecisionTimeoutSeconds = 30 };
    private readonly TransferDefinition _definition;

    public SagaEngineTests()
    {
        _notifications = new NotificationsService(_chaos);
        _definition = new TransferDefinition(_accounts, new ComplianceService(), new ExchangeService(_chaos), _notifications, _options);
        _accounts.Open(Alice, "Alice", "EUR", 50_000m);
        _accounts.Open(Bob, "Bob", "EUR", 500m);
        _accounts.Open(Carla, "Carla", "USD", 1_000m);
    }

    private SagaEngine NewEngine() => new(_store, _definition, _clock, _options);

    /// <summary>Runs the engine (moving time forward) until the saga is finished or waiting.</summary>
    private async Task<SagaState> Run(SagaEngine engine, Guid id)
    {
        for (var i = 0; i < 200; i++)
        {
            var saga = _store.Load(id)!;
            if (saga.IsFinished || saga.Status == SagaStatus.Waiting)
                return saga;
            await engine.Process();
            _clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        throw new InvalidOperationException("The saga did not settle.");
    }

    private decimal Balance(Guid id) => _accounts.Get(id)!.Balance;
    private decimal Available(Guid id) => _accounts.Get(id)!.Available;
    private static IEnumerable<string> Messages(SagaState saga) => saga.Log.Select(j => j.Message);

    [Fact]
    public async Task A_transfer_goes_through_the_six_steps_in_order()
    {
        var engine = NewEngine();

        var saga = await Run(engine, engine.Start(Alice, Bob, 100m).Id);

        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Equal(["ReserveFunds", "CheckCompliance", "ConvertCurrencies", "CreditBeneficiary", "CaptureFunds", "Notify"],
            saga.CompletedSteps);
        Assert.Equal((49_900m, 600m), (Balance(Alice), Balance(Bob)));
        Assert.Single(_notifications.Sent);
    }

    [Fact]
    public async Task A_transfer_to_an_account_in_dollars_is_converted()
    {
        var engine = NewEngine();

        var saga = await Run(engine, engine.Start(Alice, Carla, 100m).Id);

        Assert.Equal((108.32m, "USD"), (saga.CreditedAmount, saga.TargetCurrency));
        Assert.Equal(1_108.32m, Balance(Carla));
    }

    [Fact]
    public async Task A_refusal_at_the_first_step_cancels_with_nothing_to_undo()
    {
        var engine = NewEngine();

        var saga = await Run(engine, engine.Start(Bob, Alice, 9_999m).Id);

        Assert.Equal(SagaStatus.Cancelled, saga.Status);
        Assert.Contains("Insufficient available balance", saga.Reason);
        Assert.Equal(500m, Balance(Bob));
    }

    [Fact]
    public async Task A_transient_failure_is_absorbed_by_retries()
    {
        _chaos.ExchangeFailuresLeft = 2;
        var engine = NewEngine();

        var saga = await Run(engine, engine.Start(Alice, Carla, 100m).Id);

        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Equal(2, Messages(saga).Count(m => m.StartsWith("✖ ConvertCurrencies")));
    }

    [Fact]
    public async Task A_lasting_failure_triggers_the_compensation_in_reverse_order()
    {
        _chaos.ExchangeDown = true;
        var engine = NewEngine();

        var saga = await Run(engine, engine.Start(Alice, Carla, 100m).Id);

        Assert.Equal(SagaStatus.Cancelled, saga.Status);
        Assert.Equal(3, Messages(saga).Count(m => m.StartsWith("✖ ConvertCurrencies")));  // 3 retries…
        Assert.Contains("impossible after 4 attempts", saga.Reason);                        // …then give up at the 4th failure
        Assert.Contains("↩ ReserveFunds undone", Messages(saga));
        Assert.Equal(50_000m, Available(Alice));  // the reservation was released
        Assert.Equal(1_000m, Balance(Carla));
    }

    [Theory]
    [InlineData(true, SagaStatus.Completed)]
    [InlineData(false, SagaStatus.Cancelled)]
    public async Task A_large_transfer_waits_for_an_analyst_decision(bool approved, SagaStatus expected)
    {
        var engine = NewEngine();
        var id = engine.Start(Alice, Bob, 12_000m).Id;

        var waiting = await Run(engine, id);
        Assert.Equal(SagaStatus.Waiting, waiting.Status);
        Assert.Equal(38_000m, Available(Alice));  // the funds are reserved during the wait

        await engine.Signal(id, approved, "file checked");
        var saga = await Run(engine, id);

        Assert.Equal(expected, saga.Status);
        Assert.Equal(approved ? 12_500m : 500m, Balance(Bob));
        Assert.Equal(approved ? 38_000m : 50_000m, Available(Alice));
    }

    [Fact]
    public async Task Without_a_decision_before_the_deadline_the_saga_is_compensated()
    {
        var engine = NewEngine();
        var id = engine.Start(Alice, Bob, 12_000m).Id;
        await Run(engine, id);

        _clock.Advance(TimeSpan.FromSeconds(31));
        await engine.Process();  // the engine notices that the deadline is missed
        var saga = await Run(engine, id);

        Assert.Equal(SagaStatus.Cancelled, saga.Status);
        Assert.Contains("deadline", saga.Reason);
        Assert.Equal(50_000m, Available(Alice));
    }

    [Fact]
    public async Task After_the_point_of_no_return_there_is_no_more_compensation()
    {
        _chaos.NotificationsDown = true;
        var engine = NewEngine();

        var saga = await Run(engine, engine.Start(Alice, Bob, 100m).Id);

        Assert.Equal(SagaStatus.ManualInterventionRequired, saga.Status);
        Assert.Equal((49_900m, 600m), (Balance(Alice), Balance(Bob)));  // the money did go through: nothing is undone
    }

    [Fact]
    public async Task A_new_engine_resumes_the_sagas_where_the_old_one_left_them()
    {
        var first = NewEngine();
        var id = first.Start(Alice, Carla, 100m).Id;
        await first.Process();
        await first.Process();  // ReserveFunds and CheckCompliance done… then "crash"

        var second = NewEngine();  // no state shared with the first one, except the store
        Assert.Equal(["ReserveFunds", "CheckCompliance"], _store.Load(id)!.CompletedSteps);
        var saga = await Run(second, id);

        Assert.Equal(SagaStatus.Completed, saga.Status);
        Assert.Equal(1_108.32m, Balance(Carla));
    }

    [Fact]
    public async Task Replaying_a_step_already_done_at_a_participant_has_no_effect()
    {
        // Crash between the call to the participant and the saving of the state: the step will be replayed.
        var saga = Guid.NewGuid();
        await _accounts.Reserve(saga, Alice, 100m);
        await _accounts.Reserve(saga, Alice, 100m);
        await _accounts.Credit(saga, Bob, 100m);
        await _accounts.Credit(saga, Bob, 100m);

        Assert.Equal((49_900m, 600m), (Available(Alice), Balance(Bob)));
    }
}
