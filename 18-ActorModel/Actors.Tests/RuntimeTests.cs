using Actors.Runtime;

namespace Actors.Tests;

/// <summary>A clock moved forward by hand: passivation and quarantine tested without waiting.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

/// <summary>What the toy actors report to the test, from the outside (atomic counters: several actors write to them).</summary>
public sealed class Probe
{
    public int InProgress, MaxInProgress, Starts, Stops, Alive, MaxAlive;
    public readonly TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Max(ref int max, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, value, current) != current) { }
    }
}

public sealed record Increment;
public sealed record Read;
public sealed record Note(int Value);
public sealed record Notes;
public sealed record Crash;
public sealed record Slow(int Ms);
public sealed record Block;

/// <summary>
/// A toy actor. Its counter is incremented in the most dangerous way possible (read, yield the thread, write):
/// without the "one message at a time" guarantee, increments would be lost. Its state is NOT durable.
/// </summary>
public sealed class Toy(Probe probe) : Actor
{
    private int _counter;
    private readonly List<int> _notes = [];

    public override async Task Start()
    {
        Interlocked.Increment(ref probe.Starts);
        Probe.Max(ref probe.MaxAlive, Interlocked.Increment(ref probe.Alive));
        await Task.Delay(1);
    }

    public override async Task Stop()
    {
        await Task.Delay(5);  // a stop that takes its time: another incarnation could start in the meantime
        Interlocked.Decrement(ref probe.Alive);
        Interlocked.Increment(ref probe.Stops);
    }

    public override async Task<object?> Receive(object message)
    {
        Probe.Max(ref probe.MaxInProgress, Interlocked.Increment(ref probe.InProgress));
        try
        {
            switch (message)
            {
                case Increment:
                    var read = _counter;
                    await Task.Yield();
                    _counter = read + 1;
                    return _counter;
                case Read:
                    return _counter;
                case Note n:
                    _notes.Add(n.Value);
                    return null;
                case Notes:
                    return _notes.ToList();
                case Slow l:
                    await Task.Delay(l.Ms);
                    return "done";
                case Block:
                    await probe.Gate.Task;
                    return "unblocked";
                case Crash:
                    throw new InvalidOperationException("boom");
                default:
                    throw new NotSupportedException();
            }
        }
        finally
        {
            Interlocked.Decrement(ref probe.InProgress);
        }
    }
}

public class RuntimeTests
{
    private readonly ManualClock _clock = new();
    private readonly Probe _probe = new();
    private readonly ActorSystem _system;

    public RuntimeTests()
    {
        _system = new ActorSystem(_clock);
        _system.Register("toy", () => new Toy(_probe), new SupervisionStrategy(2, TimeSpan.FromMinutes(1)));
    }

    private ActorRef Toy(string key = "a") => _system.Actor("toy", key);

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task An_actor_handles_only_one_message_at_a_time()
    {
        var toy = Toy();

        await Task.WhenAll(Enumerable.Range(0, 1_000).Select(_ => Task.Run(() => toy.Ask(new Increment()))));

        Assert.Equal(1_000, await toy.Ask(new Read()));  // no increment lost, without the slightest lock
        Assert.Equal(1, _probe.MaxInProgress);
    }

    [Fact]
    public async Task Different_actors_work_in_parallel()
    {
        await Task.WhenAll(Toy("a").Ask(new Slow(200)), Toy("b").Ask(new Slow(200)));

        Assert.Equal(2, _probe.MaxInProgress);
    }

    [Fact]
    public async Task The_messages_of_a_sender_are_handled_in_sending_order()
    {
        var toy = Toy();
        for (var i = 0; i < 100; i++)
            toy.Tell(new Note(i));  // "Tell": nothing is awaited

        Assert.Equal(Enumerable.Range(0, 100), (List<int>)(await toy.Ask(new Notes()))!);
    }

    [Fact]
    public async Task The_first_message_activates_the_actor_and_inactivity_passivates_it()
    {
        var toy = Toy();
        Assert.Equal(0, _system.State().Active);  // an address activates nothing

        await toy.Ask(new Increment());
        Assert.Equal(1, _system.State().Active);
        Assert.Equal(0, _system.Passivate(TimeSpan.FromSeconds(30)));  // too recent

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, _system.Passivate(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, _system.State().Active);
        await WaitFor(() => _probe.Stops == 1);

        // The next message wakes it up: new incarnation, blank memory (this toy stores nothing).
        Assert.Equal(0, await toy.Ask(new Read()));
        Assert.Equal(2, _probe.Starts);
        Assert.Equal(2, _system.State().Activations);
    }

    [Fact]
    public async Task A_busy_actor_is_never_passivated()
    {
        var toy = Toy();
        var blocked = toy.Ask(new Block());
        await WaitFor(() => _probe.InProgress == 1);
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, _system.Passivate(TimeSpan.Zero));

        _probe.Gate.SetResult();
        Assert.Equal("unblocked", await blocked);
    }

    [Fact]
    public async Task Never_two_incarnations_of_the_same_actor_at_the_same_time()
    {
        var toy = Toy();

        // Messages and passivations collide: each passivation starts a slow stop, and the next message
        // a reactivation. The new incarnation must wait for the old one to finish stopping.
        for (var round = 0; round < 30; round++)
        {
            var messages = Enumerable.Range(0, 10).Select(_ => Task.Run(() => toy.Ask(new Increment())));
            var passivations = Enumerable.Range(0, 5).Select(_ => Task.Run(() => _system.Passivate(TimeSpan.Zero)));
            await Task.WhenAll([.. messages, .. passivations]);
        }

        Assert.True(_probe.Starts > 1, "the test must cause reactivations");
        Assert.Equal(1, _probe.MaxAlive);
    }

    [Fact]
    public async Task A_failure_replaces_the_incarnation_without_losing_the_next_messages()
    {
        var toy = Toy();
        var blocked = toy.Ask(new Block());  // everything that follows piles up in the mailbox
        var one = toy.Ask(new Increment());
        var two = toy.Ask(new Increment());
        var boom = toy.Ask(new Crash());
        var after = toy.Ask(new Increment());
        await WaitFor(() => _probe.InProgress == 1);

        _probe.Gate.SetResult();

        Assert.Equal(1, await one);
        Assert.Equal(2, await two);
        var failure = await Assert.ThrowsAsync<ActorFailedException>(() => boom);
        Assert.Equal("toy/a", failure.Path);
        // The next message is handled by a FRESH incarnation: the unstored memory started again from zero.
        Assert.Equal(1, await after);
        Assert.Equal(1, _system.State().Restarts);
        Assert.Contains(_system.Log(), e => e.Contains("toy/a restarted after a failure: boom"));
    }

    [Fact]
    public async Task A_failure_does_not_affect_the_other_actors()
    {
        await Toy("a").Ask(new Increment());
        await Toy("a").Ask(new Increment());

        await Assert.ThrowsAsync<ActorFailedException>(() => Toy("b").Ask(new Crash()));

        Assert.Equal(2, await Toy("a").Ask(new Read()));
        Assert.Equal(0, _system.State().Actors.Single(a => a.Path == "toy/a").Restarts);
    }

    [Fact]
    public async Task Too_many_failures_stop_the_actor_until_the_end_of_the_window()
    {
        var toy = Toy();
        for (var i = 0; i < 3; i++)  // strategy: 2 restarts per minute; the 3rd failure stops the actor
            await Assert.ThrowsAsync<ActorFailedException>(() => toy.Ask(new Crash()));
        await WaitFor(() => _system.State().Stops == 1);  // the caller is told of the failure before the supervisor's decision

        await Assert.ThrowsAsync<ActorUnavailableException>(() => toy.Ask(new Read()));
        toy.Tell(new Increment());  // nobody will receive it
        var state = _system.State();
        Assert.Equal((2, 1, 1, 0), (state.Restarts, state.Stops, state.DeadLetters, state.Active));

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, await toy.Ask(new Read()));  // quarantine over: reactivated from scratch
    }

    [Fact]
    public async Task A_request_that_takes_too_long_expires_without_the_message_being_lost()
    {
        var toy = Toy();
        _ = toy.Ask(new Block());
        var pendingIncrement = toy.Ask(new Increment());

        await Assert.ThrowsAsync<TimeoutException>(() => toy.Ask(new Read(), TimeSpan.FromMilliseconds(50)));

        _probe.Gate.SetResult();
        Assert.Equal(1, await pendingIncrement);
        Assert.Equal(1, await toy.Ask(new Read()));
    }

    [Fact]
    public void An_unknown_actor_type_is_refused_from_the_address() =>
        Assert.Throws<ArgumentException>(() => _system.Actor("unknown", "1"));
}
