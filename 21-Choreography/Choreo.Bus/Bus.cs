using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Choreo.Bus;

/// <summary>
/// A fact that happened, in the past tense ("FundsReserved", not "ReserveFunds"). Whoever publishes it does not know who listens to it.
/// </summary>
public abstract record Event
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The process it belongs to (here, the transfer identifier): the only thread linking the events.</summary>
    public string Correlation { get; init; } = "";

    /// <summary>The event that caused it (null if it comes from outside: a request, a timer, a human).</summary>
    public Guid? Cause { get; init; }

    public string Emitter { get; init; } = "";
    public DateTimeOffset Date { get; init; }

    public string Type => GetType().Name;

    /// <summary>A new event, in reaction to this one: same correlation, and this one as cause.</summary>
    public T Next<T>(T next, string emitter) where T : Event =>
        next with { Correlation = Correlation, Cause = Id, Emitter = emitter };
}

public sealed record BusOptions
{
    /// <summary>Attempts to deliver an event to a subscriber, before putting it in the dead letters.</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>Probability of delivering an event TWICE (the "at least once" of real brokers, exaggerated).</summary>
    public double DuplicateProbability { get; init; }

    public int Seed { get; init; } = 42;
}

public sealed record DeadLetter(string Subscriber, Event Event, int Attempts, string Error);

public sealed record SubscriberState(string Subscriber, IReadOnlyList<string> Listens, int Pending, long Delivered, long Failures);

/// <summary>
/// The broker: each subscriber has ITS queue and empties it at its own pace; a slow or failed subscriber does not delay the others.
/// A handling that fails is retried, then put in the dead letters. Everything published is kept in the log.
/// </summary>
public sealed class Bus : IAsyncDisposable
{
    private readonly BusOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly List<Event> _log = [];
    private readonly ConcurrentQueue<DeadLetter> _deadLetters = new();
    private readonly Lock _lock = new();
    private readonly Random _random;
    private readonly CancellationTokenSource _stopping = new();
    private int _inProgress;

    public Bus(BusOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new BusOptions();
        _clock = clock ?? TimeProvider.System;
        _random = new Random(_options.Seed);
    }

    /// <summary>Subscribe to an event type. A subscriber may listen to several types: they arrive in its queue, in order.</summary>
    public void Subscribe<T>(string subscriber, Func<T, Task> handle) where T : Event
    {
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(subscriber, out var subscription))
            {
                subscription = new Subscription(subscriber);
                subscription.Loop = Task.Run(() => Deliver(subscription));
                _subscriptions[subscriber] = subscription;
            }
            subscription.Handlers[typeof(T)] = e => handle((T)e);
        }
    }

    public void Subscribe<T>(string subscriber, Action<T> handle) where T : Event =>
        Subscribe<T>(subscriber, e =>
        {
            handle(e);
            return Task.CompletedTask;
        });

    /// <summary>Publish: drop into the queue of each interested subscriber, without waiting for anyone.</summary>
    public T Publish<T>(T @event) where T : Event
    {
        var dated = @event.Date == default ? @event with { Date = _clock.GetUtcNow() } : @event;
        lock (_lock)
        {
            _log.Add(dated);
            foreach (var subscription in _subscriptions.Values.Where(a => a.Handlers.ContainsKey(dated.GetType())))
            {
                Enqueue(subscription, dated);
                if (_options.DuplicateProbability > 0 && _random.NextDouble() < _options.DuplicateProbability)
                    Enqueue(subscription, dated);  // delivered twice: the subscriber must notice it
            }
        }
        return dated;
    }

    private void Enqueue(Subscription subscription, Event @event)
    {
        Interlocked.Increment(ref _inProgress);
        subscription.Queue.Writer.TryWrite(@event);
    }

    private async Task Deliver(Subscription subscription)
    {
        try
        {
            await foreach (var @event in subscription.Queue.Reader.ReadAllAsync(_stopping.Token))
            {
                Func<Event, Task> handler;
                lock (_lock)
                    handler = subscription.Handlers[@event.GetType()];

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await handler(@event);
                        Interlocked.Increment(ref subscription.Delivered);
                        break;
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Interlocked.Increment(ref subscription.Failures);
                        if (attempt >= _options.MaxAttempts)
                        {
                            // Nobody will know about it, except whoever reads the dead letters: the process is left hanging.
                            _deadLetters.Enqueue(new DeadLetter(subscription.Name, @event, attempt, e.Message));
                            break;
                        }
                        await Task.Delay(10 * attempt, _stopping.Token);
                    }
                }
                Interlocked.Decrement(ref _inProgress);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    /// <summary>All the published events, in order.</summary>
    public IReadOnlyList<Event> Log()
    {
        lock (_lock) return _log.ToList();
    }

    public IReadOnlyList<Event> Log(string correlation)
    {
        lock (_lock) return _log.Where(e => e.Correlation == correlation).ToList();
    }

    public IReadOnlyList<DeadLetter> DeadLetters() => _deadLetters.ToList();

    public IReadOnlyList<SubscriberState> Subscribers()
    {
        lock (_lock)
            return _subscriptions.Values.OrderBy(a => a.Name).Select(a => new SubscriberState(a.Name,
                a.Handlers.Keys.Select(t => t.Name).Order().ToList(), a.Queue.Reader.Count,
                Interlocked.Read(ref a.Delivered), Interlocked.Read(ref a.Failures))).ToList();
    }

    /// <summary>Waits until nothing flows any more: all the chain reactions are over.</summary>
    public async Task WaitForCalm(TimeSpan? timeout = null)
    {
        var limit = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (Volatile.Read(ref _inProgress) > 0)
        {
            if (DateTime.UtcNow > limit)
                throw new TimeoutException("The bus did not calm down.");
            await Task.Delay(5);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        List<Task> loops;
        lock (_lock) loops = _subscriptions.Values.Select(a => a.Loop!).ToList();
        await Task.WhenAll(loops);
    }

    private sealed class Subscription(string name)
    {
        public string Name { get; } = name;
        public Dictionary<Type, Func<Event, Task>> Handlers { get; } = new();
        public Channel<Event> Queue { get; } = Channel.CreateUnbounded<Event>();
        public Task? Loop { get; set; }
        public long Delivered, Failures;
    }
}
