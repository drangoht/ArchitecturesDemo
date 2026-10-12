namespace Actors.Runtime;

/// <summary>
/// The ADDRESS of an actor, not the actor itself: it can be kept, copied, passed on in a message.
/// Whether it is active, passivated or restarting changes nothing for the caller (location transparency:
/// in Orleans or Akka, it could even live on another machine).
/// </summary>
public sealed record ActorRef
{
    private readonly ActorSystem _system;

    internal ActorRef(ActorSystem system, string path)
    {
        _system = system;
        Path = path;
    }

    public string Path { get; }

    /// <summary>"Tell": drop a message and move on, without waiting for a response.</summary>
    public void Tell(object message) => _system.Deliver(Path, new Envelope(message, null));

    /// <summary>
    /// "Ask": drop a message and wait for the response.
    /// Throws <see cref="ActorFailedException"/> if the message made the actor crash,
    /// <see cref="ActorUnavailableException"/> if it is stopped by its supervisor, <see cref="TimeoutException"/> if it takes too long.
    /// </summary>
    public async Task<object?> Ask(object message, TimeSpan? timeout = null)
    {
        var response = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _system.Deliver(Path, new Envelope(message, response));
        var wait = timeout ?? _system.AskTimeout;
        try
        {
            return await response.Task.WaitAsync(wait);
        }
        catch (TimeoutException)
        {
            // The message stays in the mailbox and will be processed later: only the caller stopped waiting.
            throw new TimeoutException($"{Path} did not answer within {wait.TotalMilliseconds:0} ms.");
        }
    }

    public override string ToString() => Path;
}

/// <summary>A message and, for a request, what is needed to answer it.</summary>
internal sealed record Envelope(object Message, TaskCompletionSource<object?>? Response);
