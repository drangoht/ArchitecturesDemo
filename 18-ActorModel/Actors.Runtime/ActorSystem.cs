using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Actors.Runtime;

/// <summary>
/// The actor system: it creates the actors on demand, hands them their messages, supervises them and
/// puts them to sleep when they are no longer in use.
/// <para>
/// VIRTUAL actors (the Orleans model): an actor "account/42" always exists in principle; you neither create
/// nor destroy it. The first message activates it (an <i>incarnation</i> in memory), inactivity passivates it,
/// the next message reactivates it. Central guarantee: at most ONE incarnation per path at a time.
/// </para>
/// </summary>
public sealed class ActorSystem(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, ActorType> _types = new();

    // Everything that decides WHICH incarnation receives a message is protected by this lock (the runtime needs it;
    // the actors never do).
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Activation> _activations = new();
    private readonly Dictionary<string, Task> _finishingIncarnations = new();
    private readonly Dictionary<string, DateTimeOffset> _quarantines = new();

    private readonly ConcurrentQueue<string> _log = new();
    private long _totalActivations, _passivations, _restarts, _stops, _deadLetters;

    /// <summary>Default timeout of a request: an actor that does not answer does not block the caller forever.</summary>
    public TimeSpan AskTimeout { get; init; } = TimeSpan.FromSeconds(5);

    private sealed record ActorType(Func<Actor> Factory, SupervisionStrategy Strategy);

    /// <summary>Declares an actor type: "account" → how to build an incarnation, how to supervise it.</summary>
    public void Register(string type, Func<Actor> factory, SupervisionStrategy? strategy = null) =>
        _types[type] = new ActorType(factory, strategy ?? SupervisionStrategy.Default);

    /// <summary>The address of an actor. Does not activate it: only a message does.</summary>
    public ActorRef Actor(string type, string key)
    {
        if (!_types.ContainsKey(type))
            throw new ArgumentException($"Unknown actor type: \"{type}\".", nameof(type));
        return new ActorRef(this, $"{type}/{key}");
    }

    public ActorRef Actor(string type, Guid key) => Actor(type, key.ToString());

    /// <summary>The only entry point of the messages: find (or create) the incarnation, drop into its mailbox.</summary>
    internal void Deliver(string path, Envelope envelope)
    {
        lock (_lock)
        {
            if (_quarantines.TryGetValue(path, out var until))
            {
                if (_clock.GetUtcNow() < until)
                {
                    Refuse(envelope, new ActorUnavailableException(path, until));
                    return;
                }
                _quarantines.Remove(path);
            }

            if (!_activations.TryGetValue(path, out var activation))
                activation = Activate(path);

            // Unbounded mailbox never completed as long as the activation is in the dictionary: the write always succeeds.
            activation.Mailbox.Writer.TryWrite(envelope);
        }
    }

    // Called under the lock.
    private Activation Activate(string path)
    {
        var separator = path.IndexOf('/');
        var (type, key) = (path[..separator], path[(separator + 1)..]);
        var activation = new Activation(path, _types[type], new ActorContext(this, type, key), _clock.GetUtcNow());

        // If a previous incarnation is still finishing stopping (passivation), the new one waits for it before starting:
        // never two incarnations of the same actor at the same time, not even for a fraction of a second.
        _finishingIncarnations.Remove(path, out var previous);
        activation.Loop = Task.Run(() => Run(activation, previous));

        _activations[path] = activation;
        Interlocked.Increment(ref _totalActivations);
        return activation;
    }

    /// <summary>The life of an activation: start, then handle the messages one by one until the mailbox is completed.</summary>
    private async Task Run(Activation activation, Task? previous)
    {
        if (previous is not null)
            await previous;

        if (!await Incarnate(activation))
        {
            await StopActivation(activation, "its start failed");
            return;
        }

        await foreach (var envelope in activation.Mailbox.Reader.ReadAllAsync())
        {
            activation.Processing = true;
            object? response = null;
            Exception? failure = null;
            try
            {
                // THE key point of the model: only one message at a time, each handled to the end before the next one.
                response = await activation.Instance!.Receive(envelope.Message);
            }
            catch (Exception e)
            {
                failure = e;
            }
            activation.MessagesProcessed++;
            activation.LastActivity = _clock.GetUtcNow();

            if (failure is null)
            {
                activation.Processing = false;
                envelope.Response?.TrySetResult(response);  // the bookkeeping is up to date when the caller resumes
                continue;
            }

            envelope.Response?.TrySetException(new ActorFailedException(activation.Path, failure));
            var carryOn = await Supervise(activation, failure);
            activation.Processing = false;
            if (!carryOn)
                return;
        }

        // Mailbox completed by the passivation: the incarnation shuts down cleanly.
        await Deincarnate(activation);
    }

    private async Task<bool> Incarnate(Activation activation)
    {
        try
        {
            var instance = activation.Type.Factory();
            instance.Context = activation.Context;
            await instance.Start();
            activation.Instance = instance;
            return true;
        }
        catch (Exception e)
        {
            Note($"{activation.Path}: start failed ({e.Message})");
            return false;
        }
    }

    private async Task Deincarnate(Activation activation)
    {
        try
        {
            if (activation.Instance is { } instance)
                await instance.Stop();
        }
        catch (Exception e)
        {
            Note($"{activation.Path}: error on stop ignored ({e.Message})");
        }
        activation.Instance = null;
    }

    /// <summary>
    /// A failure: we do not repair, we replace. The faulty incarnation (and its in-memory state, perhaps inconsistent)
    /// is thrown away; a fresh one reloads the durable state and takes the NEXT messages, which were waiting in the mailbox.
    /// </summary>
    private async Task<bool> Supervise(Activation activation, Exception failure)
    {
        var now = _clock.GetUtcNow();
        var strategy = activation.Type.Strategy;
        activation.Failures.RemoveAll(p => now - p > strategy.Window);
        activation.Failures.Add(now);

        if (activation.Failures.Count > strategy.MaxRestarts)
        {
            await StopActivation(activation, $"{activation.Failures.Count} failures in {strategy.Window.TotalSeconds:0} s ({failure.Message})");
            return false;
        }

        await Deincarnate(activation);
        if (!await Incarnate(activation))
        {
            await StopActivation(activation, "its restart failed");
            return false;
        }
        activation.Restarts++;
        Interlocked.Increment(ref _restarts);
        Note($"{activation.Path} restarted after a failure: {failure.Message}");
        return true;
    }

    /// <summary>Final stop decided by the supervisor: removal, quarantine, and the pending messages are refused.</summary>
    private async Task StopActivation(Activation activation, string reason)
    {
        var until = _clock.GetUtcNow() + activation.Type.Strategy.Window;
        lock (_lock)
        {
            _activations.Remove(activation.Path);
            _finishingIncarnations[activation.Path] = activation.Loop!;
            _quarantines[activation.Path] = until;
            activation.Mailbox.Writer.TryComplete();
        }
        while (activation.Mailbox.Reader.TryRead(out var pending))
            Refuse(pending, new ActorUnavailableException(activation.Path, until));

        Interlocked.Increment(ref _stops);
        Note($"{activation.Path} STOPPED by its supervisor: {reason}; quarantine until {until:HH:mm:ss}");
        await Deincarnate(activation);
    }

    private void Refuse(Envelope envelope, Exception reason)
    {
        if (envelope.Response is { } response)
            response.TrySetException(reason);
        else
            Interlocked.Increment(ref _deadLetters);  // a "Tell" nobody will receive
    }

    /// <summary>
    /// Puts to sleep the actors idle for <paramref name="inactivity"/>: their memory is released, their durable state stays.
    /// An actor with pending messages or a message being processed is never passivated.
    /// </summary>
    public int Passivate(TimeSpan inactivity)
    {
        var now = _clock.GetUtcNow();
        List<Activation> asleep;
        lock (_lock)
        {
            asleep = _activations.Values
                .Where(a => !a.Processing && a.Mailbox.Reader.Count == 0 && now - a.LastActivity >= inactivity)
                .ToList();
            foreach (var activation in asleep)
            {
                _activations.Remove(activation.Path);
                _finishingIncarnations[activation.Path] = activation.Loop!;
                // Complete the mailbox: the loop finishes what it has already taken, then calls Stop().
                activation.Mailbox.Writer.TryComplete();
            }
        }
        if (asleep.Count > 0)
        {
            var names = string.Join(", ", asleep.Take(5).Select(a => a.Path));
            Note($"{asleep.Count} actor(s) passivated: {names}{(asleep.Count > 5 ? "…" : "")}");
        }
        Interlocked.Add(ref _passivations, asleep.Count);
        return asleep.Count;
    }

    /// <summary>Periodic passivation, as long as the token is not cancelled (in the API: a background service).</summary>
    public async Task PassivatePeriodically(TimeSpan inactivity, TimeSpan period, CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(period, _clock);
        while (await timer.WaitForNextTickAsync(stopping))
            Passivate(inactivity);
    }

    public SystemState State()
    {
        lock (_lock)
        {
            var actors = _activations.Values.OrderBy(a => a.Path)
                .Select(a => new ActivationState(a.Path, a.ActivatedAt, a.MessagesProcessed, a.Mailbox.Reader.Count, a.Restarts))
                .ToList();
            return new SystemState(actors.Count, Interlocked.Read(ref _totalActivations), Interlocked.Read(ref _passivations),
                Interlocked.Read(ref _restarts), Interlocked.Read(ref _stops), Interlocked.Read(ref _deadLetters), actors);
        }
    }

    /// <summary>The latest supervision events, from the most recent to the oldest.</summary>
    public IReadOnlyList<string> Log() => _log.Reverse().ToList();

    private void Note(string @event)
    {
        _log.Enqueue($"{_clock.GetUtcNow():HH:mm:ss.fff} {@event}");
        while (_log.Count > 200)
            _log.TryDequeue(out _);
    }

    /// <summary>An incarnation and its mailbox.</summary>
    private sealed class Activation(string path, ActorType type, ActorContext context, DateTimeOffset now)
    {
        public string Path { get; } = path;
        public ActorType Type { get; } = type;
        public ActorContext Context { get; } = context;
        public DateTimeOffset ActivatedAt { get; } = now;

        /// <summary>
        /// Several writers (any thread), ONE single reader: the actor's loop. (The SingleReader option
        /// would optimize it, but such a channel can no longer count its messages, which the passivation needs.)
        /// </summary>
        public Channel<Envelope> Mailbox { get; } = Channel.CreateUnbounded<Envelope>();

        public Task? Loop { get; set; }
        public Actor? Instance { get; set; }
        public volatile bool Processing;
        private long _lastActivity = now.UtcTicks;

        /// <summary>Written by the loop, read by the passivation (another thread): hence the atomic access.</summary>
        public DateTimeOffset LastActivity
        {
            get => new(Interlocked.Read(ref _lastActivity), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastActivity, value.UtcTicks);
        }

        public long MessagesProcessed { get; set; }
        public int Restarts { get; set; }
        public List<DateTimeOffset> Failures { get; } = [];
    }
}
