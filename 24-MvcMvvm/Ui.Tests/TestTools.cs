using Ui.Bank;
using Ui.Presentation;

namespace Ui.Tests;

/// <summary>A stopped clock: 11 October 2026 at 2:30 pm, Paris time.</summary>
public sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 12, 30, 0, TimeSpan.Zero);
}

/// <summary>
/// The real in-memory bank, with a remote control: a call can be HELD (it waits until released) to
/// observe the screen during the wait, or to make the responses arrive out of order. No Thread.Sleep.
/// </summary>
public sealed class RemoteControlledBank : IBank
{
    private readonly Dictionary<(string Call, Guid? Account), TaskCompletionSource> _held = [];

    public InMemoryBank Real { get; } = new(new FixedClock());

    public int TransfersRequested { get; private set; }

    /// <summary>The next "Transfer" or "Operations" call (of this account) will wait for <see cref="Release"/>.</summary>
    public void Hold(string call, Guid? account = null)
    {
        lock (_held)
            _held[(call, account)] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release(string call, Guid? account = null)
    {
        TaskCompletionSource? held;
        lock (_held)
            _held.Remove((call, account), out held);
        held?.SetResult();
    }

    private Task Pass(string call, Guid? account)
    {
        lock (_held)
            return _held.TryGetValue((call, account), out var held) ? held.Task : Task.CompletedTask;
    }

    public Task<IReadOnlyList<Account>> AccountsAsync(CancellationToken cancellation = default) => Real.AccountsAsync(cancellation);

    public Task<Account> AccountAsync(Guid id, CancellationToken cancellation = default) => Real.AccountAsync(id, cancellation);

    public async Task<IReadOnlyList<Operation>> OperationsAsync(Guid account, CancellationToken cancellation = default)
    {
        await Pass("Operations", account);
        return await Real.OperationsAsync(account, cancellation);
    }

    public async Task<Transfer> TransferAsync(Guid source, Guid target, decimal amount, CancellationToken cancellation = default)
    {
        TransfersRequested++;
        await Pass("Transfer", null);
        return await Real.TransferAsync(source, target, amount, cancellation);
    }
}

/// <summary>A confirmation that answers what it is told, and remembers the questions asked.</summary>
public sealed class ProgrammedConfirmation(bool answer) : IConfirmation
{
    public List<string> Questions { get; } = [];

    public Task<bool> AskAsync(string question)
    {
        Questions.Add(question);
        return Task.FromResult(answer);
    }
}
