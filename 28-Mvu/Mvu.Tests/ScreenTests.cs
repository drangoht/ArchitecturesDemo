using Mvu.Bank;
using Mvu.Console;
using Mvu.Runtime;
using Ui.Bank;

namespace Mvu.Tests;

/// <summary>Everything together: the program, the runtime, the effects, the real bank of demo 24, the console.</summary>
public class ScreenTests
{
    [Fact]
    public async Task A_session_at_the_console_from_typing_to_the_confirmed_transfer()
    {
        var input = new StringReader(string.Join("\n",
            "pick from 1", "pick to 3", "type amount 2000", "type amount 250", "click transfer", "click transfer",
            "pick from 2", "type amount 1200", "click transfer", "click yes", "quit"));
        var output = new StringWriter();
        var renderer = new ConsoleRenderer<Model, Msg, Effect>(input, output);
        await using var runtime = new Runtime<Model, Msg, Effect>(
            BankScreen.Program, new BankEffects(new InMemoryBank(TimeProvider.System)).Perform, renderer.Render);

        await renderer.RunAsync(runtime).WaitAsync(TimeSpan.FromSeconds(10));

        var text = output.ToString();
        Assert.Contains("⚠ Insufficient balance: 1,000.00 € available.", text);
        Assert.Contains("✓ Transfer of 250.00 € done. New balance: 750.00 €.", text);
        Assert.Contains("(greyed out: nothing happens)", text);   // the amount was emptied: the second click does nothing
        Assert.Contains("Transfer 1,200.00 € from Alice — Savings account to Bob — Current account?", text);
        Assert.Contains("✓ Transfer of 1,200.00 € done. New balance: 6,800.00 €.", text);
        Assert.Empty(runtime.Faults);
    }

    [Fact]
    public async Task With_a_real_runtime_the_late_answer_of_a_previous_selection_is_not_shown()
    {
        var bank = new GatedBank(new InMemoryBank(TimeProvider.System));
        await using var runtime = new Runtime<Model, Msg, Effect>(BankScreen.Program, new BankEffects(bank).Perform, (_, _) => { });
        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));

        bank.Hold(InMemoryBank.AliceSavings);
        runtime.Dispatch(new AccountSelected(1));   // the savings account: its answer is held back
        runtime.Dispatch(new AccountSelected(2));   // then Bob: answered at once
        await bank.Held.WaitAsync(TimeSpan.FromSeconds(5));
        bank.Release();
        await runtime.Settled().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(InMemoryBank.Bob, runtime.Model.Selected);
        Assert.All(runtime.Model.Operations, o => Assert.DoesNotContain("Savings", o.Label));
        Assert.Contains("Operations — Bob — Current account", ViewTests.Lines(runtime.View).Select(l => l.Trim('=', ' ')));
    }

    [Fact]
    public async Task The_effects_turn_the_refusals_of_the_bank_into_messages()
    {
        var effects = new BankEffects(new InMemoryBank(TimeProvider.System));

        var refused = await effects.Perform(new SendTransfer(InMemoryBank.Bob, InMemoryBank.Alice, 600m), CancellationToken.None);
        var unknown = await effects.Perform(new LoadOperations(Guid.Empty, 7), CancellationToken.None);

        Assert.Equal(new TransferFailed("Insufficient balance: 500.00 € available."), refused);
        Assert.Equal(7, Assert.IsType<OperationsFailed>(unknown).Request);
    }

    /// <summary>The bank of demo 24, whose answer for one account can be held back until the test releases it.</summary>
    private sealed class GatedBank(IBank inner) : IBank
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid? _account;

        public Task Held => _held.Task;

        public void Hold(Guid account) => _account = account;

        public void Release() => _gate.TrySetResult();

        public Task<IReadOnlyList<Account>> AccountsAsync(CancellationToken c = default) => inner.AccountsAsync(c);

        public Task<Account> AccountAsync(Guid id, CancellationToken c = default) => inner.AccountAsync(id, c);

        public async Task<IReadOnlyList<Operation>> OperationsAsync(Guid account, CancellationToken c = default)
        {
            var operations = await inner.OperationsAsync(account, c);
            if (account == _account)
            {
                _held.TrySetResult();
                await _gate.Task;
            }
            return operations;
        }

        public Task<Transfer> TransferAsync(Guid s, Guid t, decimal a, CancellationToken c = default) => inner.TransferAsync(s, t, a, c);
    }
}
