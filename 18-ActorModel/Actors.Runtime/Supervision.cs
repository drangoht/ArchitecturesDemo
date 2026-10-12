namespace Actors.Runtime;

/// <summary>
/// What the supervisor does when an actor crashes ("let it crash"): the error is not caught in the actor,
/// the faulty incarnation is thrown away and a fresh one is started, which reloads the durable state.
/// Beyond <see cref="MaxRestarts"/> failures within the <see cref="Window"/>, restarting is no longer useful:
/// the actor is stopped and refuses messages until the end of the window (quarantine).
/// </summary>
public sealed record SupervisionStrategy(int MaxRestarts, TimeSpan Window)
{
    public static readonly SupervisionStrategy Default = new(3, TimeSpan.FromMinutes(1));
}

/// <summary>The message made the actor crash: it was restarted (or stopped), the message is lost.</summary>
public sealed class ActorFailedException(string path, Exception cause)
    : Exception($"{path} crashed while handling this message: {cause.Message}", cause)
{
    public string Path { get; } = path;
}

/// <summary>The actor is in quarantine: too many recent failures.</summary>
public sealed class ActorUnavailableException(string path, DateTimeOffset until)
    : Exception($"{path} is stopped after repeated failures, unavailable until {until:HH:mm:ss}.")
{
    public string Path { get; } = path;
}

/// <summary>Snapshot of the system, to observe it from the outside.</summary>
public sealed record SystemState(
    int Active,
    long Activations,
    long Passivations,
    long Restarts,
    long Stops,
    long DeadLetters,
    IReadOnlyList<ActivationState> Actors);

public sealed record ActivationState(string Path, DateTimeOffset ActivatedAt, long MessagesProcessed, int Pending, int Restarts);
