using System.Collections.Concurrent;

namespace Actors.Bank;

/// <summary>Where, in the handling of a message, to trigger the failure.</summary>
public enum Step
{
    /// <summary>On reception, before any change.</summary>
    Reception,

    /// <summary>The account has changed its state IN MEMORY but not yet written it to the storage: the worst moment.</summary>
    BeforeSave,

    /// <summary>The transfer has debited the source but not yet credited the target.</summary>
    AfterDebit
}

public enum Kind { Crash, Slowness }

public sealed record Trap(string Actor, Step Step, Kind Kind = Kind.Crash, int Times = 1, int LatencyMs = 0);

public sealed class SimulatedFailureException(string message) : Exception(message);

/// <summary>Failure injector for the demo (like the Chaos of demos 08 and 14): armed by the API or the tests.</summary>
public sealed class Traps
{
    private readonly ConcurrentDictionary<(string Actor, Step Step), Trap> _armed = new();

    public void Arm(Trap trap) => _armed[(trap.Actor, trap.Step)] = trap;

    public void DisarmAll() => _armed.Clear();

    public IReadOnlyList<Trap> Armed() => _armed.Values.ToList();

    /// <summary>Called by the actors at the sensitive steps: does nothing, unless a trap is armed at that spot.</summary>
    public async Task Check(string actor, Step step)
    {
        if (!_armed.TryGetValue((actor, step), out var trap))
            return;
        if (trap.Times <= 1)
            _armed.TryRemove((actor, step), out _);
        else
            _armed[(actor, step)] = trap with { Times = trap.Times - 1 };

        if (trap.Kind == Kind.Slowness)
            await Task.Delay(trap.LatencyMs);
        else
            throw new SimulatedFailureException($"simulated failure ({step})");
    }
}
