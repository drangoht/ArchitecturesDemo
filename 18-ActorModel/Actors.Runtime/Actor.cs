namespace Actors.Runtime;

/// <summary>
/// An actor: a PRIVATE state and a method that handles ONE message at a time.
/// Nobody else touches its fields; you only talk to it by dropping a message in its mailbox
/// (through an <see cref="ActorRef"/>). Hence the total absence of locks in the derived classes.
/// </summary>
public abstract class Actor
{
    /// <summary>Filled in by the runtime before <see cref="Start"/>.</summary>
    public ActorContext Context { get; internal set; } = null!;

    /// <summary>
    /// Called at each incarnation (first activation, reactivation after passivation, restart after a failure),
    /// before the first message: that is where the durable state is reloaded.
    /// </summary>
    public virtual Task Start() => Task.CompletedTask;

    /// <summary>
    /// Handles a message and returns the response (ignored if the sender does not expect one).
    /// A business refusal is a response like any other; an exception is a FAILURE, handed to the supervisor.
    /// </summary>
    public abstract Task<object?> Receive(object message);

    /// <summary>Called when the incarnation stops (passivation, restart, final stop).</summary>
    public virtual Task Stop() => Task.CompletedTask;
}

/// <summary>What the actor knows about itself and the world.</summary>
public sealed class ActorContext(ActorSystem system, string type, string key)
{
    public ActorSystem System { get; } = system;
    public string Type { get; } = type;

    /// <summary>The identity of the actor within its type (e.g. the account identifier).</summary>
    public string Key { get; } = key;

    public string Path => $"{Type}/{Key}";

    /// <summary>To talk to another actor: you never get the object, only its address.</summary>
    public ActorRef Actor(string type, string key) => System.Actor(type, key);
}
