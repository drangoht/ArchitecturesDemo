using System.Threading.Channels;

namespace Mvu.Runtime;

/// <summary>
/// An MVU program: three pure functions. Init gives the first model; Update turns a message and a model into a new
/// model and the effects to run; View turns a model into a description of the screen. None of them does any I/O.
/// </summary>
public sealed record Program<TModel, TMsg, TEffect>(
    Func<(TModel Model, IReadOnlyList<TEffect> Effects)> Init,
    Func<TMsg, TModel, (TModel Model, IReadOnlyList<TEffect> Effects)> Update,
    Func<TModel, Node<TMsg>> View);

/// <summary>
/// The loop that turns the pure functions into a living screen: it reads the messages ONE AT A TIME from a mailbox,
/// calls Update, recomputes the view, sends the differences to the renderer, and runs the effects, whose results come
/// back as new messages. The only place with I/O and concurrency: the program itself has neither.
/// </summary>
public sealed class Runtime<TModel, TMsg, TEffect> : IAsyncDisposable
{
    private readonly Program<TModel, TMsg, TEffect> _program;
    private readonly Func<TEffect, CancellationToken, Task<TMsg>> _perform;
    private readonly Action<Node<TMsg>, IReadOnlyList<Patch>> _render;
    private readonly Channel<TMsg> _mailbox = Channel.CreateUnbounded<TMsg>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<Exception> _faults = [];
    private TaskCompletionSource _idle = Completed();
    private int _work;
    private Task _loop = Task.CompletedTask;

    public Runtime(Program<TModel, TMsg, TEffect> program,
        Func<TEffect, CancellationToken, Task<TMsg>> perform,
        Action<Node<TMsg>, IReadOnlyList<Patch>> render)
    {
        _program = program;
        _perform = perform;
        _render = render;
        (Model, var effects) = program.Init();
        View = program.View(Model);
        _render(View, Diff.Compute(null, View));
        _loop = Task.Run(Loop);
        foreach (var effect in effects)
            Run(effect);
    }

    public TModel Model { get; private set; }

    public Node<TMsg> View { get; private set; }

    /// <summary>Effects that failed with an unexpected exception (the expected failures come back as messages).</summary>
    public IReadOnlyList<Exception> Faults
    {
        get { lock (_gate) return [.. _faults]; }
    }

    /// <summary>Called by the renderer when the user acts: the message waits its turn in the mailbox.</summary>
    public void Dispatch(TMsg message)
    {
        Begin();
        _mailbox.Writer.TryWrite(message);
    }

    /// <summary>Completes when no message is waiting and no effect is running: the screen is stable.</summary>
    public Task Settled()
    {
        lock (_gate)
            return _idle.Task;
    }

    private async Task Loop()
    {
        await foreach (var message in _mailbox.Reader.ReadAllAsync())
        {
            try
            {
                var (model, effects) = _program.Update(message, Model);
                var view = _program.View(model);
                var patches = Diff.Compute(View, view);
                (Model, View) = (model, view);
                if (patches.Count > 0)
                    _render(view, patches);
                foreach (var effect in effects)
                    Run(effect);
            }
            catch (Exception e)
            {
                lock (_gate)
                    _faults.Add(e);
            }
            finally
            {
                End();
            }
        }
    }

    private void Run(TEffect effect)
    {
        Begin();
        _ = Task.Run(async () =>
        {
            try
            {
                Dispatch(await _perform(effect, _stop.Token));   // the result is a message, like a click
            }
            catch (Exception e) when (e is not OperationCanceledException || !_stop.IsCancellationRequested)
            {
                lock (_gate)
                    _faults.Add(e);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                End();
            }
        });
    }

    private void Begin()
    {
        lock (_gate)
        {
            if (_work++ == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void End()
    {
        lock (_gate)
        {
            if (--_work == 0)
                _idle.TrySetResult();
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _mailbox.Writer.TryComplete();
        await _loop;
        _stop.Dispose();
    }
}
