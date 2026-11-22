using Mvu.Bank;
using Ui.Bank;

namespace Mvu.Tests;

/// <summary>
/// Update is a pure function: a message and a model in, a model and effects out. No bank, no runtime, no await,
/// no fake: each behaviour of the screen is checked with values. Compare with ViewModelTests in demo 24.
/// </summary>
public class UpdateTests
{
    public static readonly Account AliceCurrent = new(InMemoryBank.Alice, "Alice", "Current account", 1_000m);
    public static readonly Account AliceSavings = new(InMemoryBank.AliceSavings, "Alice", "Savings account", 8_000m);
    public static readonly Account Bob = new(InMemoryBank.Bob, "Bob", "Current account", 500m);

    public static Model Loaded => Apply(Model.Empty, new AccountsLoaded([AliceCurrent, AliceSavings, Bob]));

    /// <summary>The messages one after the other, the effects ignored: the state of the screen after this story.</summary>
    public static Model Apply(Model model, params Msg[] messages) =>
        messages.Aggregate(model, (current, message) => BankScreen.Update(message, current).Item1);

    private static Model ReadyToTransfer(string amount) =>
        Apply(Loaded, new SourceChosen(0), new TargetChosen(2), new AmountTyped(amount));

    [Fact]
    public void At_start_the_screen_only_asks_for_the_accounts()
    {
        var (model, effects) = BankScreen.Init();

        Assert.Equal(Model.Empty, model);
        Assert.Equal([new LoadAccounts()], effects);
    }

    [Fact]
    public void Once_loaded_the_first_account_is_selected_and_its_operations_asked_for()
    {
        var (model, effects) = BankScreen.Update(new AccountsLoaded([AliceCurrent, AliceSavings, Bob]), Model.Empty);

        Assert.Equal((InMemoryBank.Alice, true), (model.Selected!.Value, model.LoadingOperations));
        Assert.Equal([new LoadOperations(InMemoryBank.Alice, 1)], effects);
    }

    [Fact]
    public void A_late_answer_for_an_account_no_longer_selected_is_ignored()
    {
        var savingsOperations = new[] { new Operation(DateTimeOffset.UnixEpoch, "Savings", 1m) };
        var bobOperations = new[] { new Operation(DateTimeOffset.UnixEpoch, "Bob", 2m) };
        var model = Apply(Loaded, new AccountSelected(1), new AccountSelected(2));   // requests 2 (savings), then 3 (Bob)

        model = Apply(model, new OperationsLoaded(3, bobOperations), new OperationsLoaded(2, savingsOperations));

        Assert.Equal(InMemoryBank.Bob, model.Selected);
        Assert.Equal("Bob", model.Operations.Single().Label);
    }

    [Theory]
    [InlineData("abc", "Enter an amount, for example 250.50.")]
    [InlineData("-5", "The amount must be positive.")]
    [InlineData("10.001", "The amount is given to the cent.")]
    [InlineData("2000", "Insufficient balance: 1,000.00 € available.")]
    [InlineData("1,000 €", null)]
    [InlineData("", null)]
    public void The_amount_is_checked_at_each_keystroke(string typed, string? error)
    {
        Assert.Equal(error, ReadyToTransfer(typed).AmountError);
    }

    [Fact]
    public void The_balance_error_follows_the_source_without_anything_to_call()
    {
        var model = Apply(Loaded, new SourceChosen(2), new TargetChosen(0), new AmountTyped("600"));
        Assert.NotNull(model.AmountError);   // Bob has 500 €

        model = Apply(model, new SwapClicked());   // now from Alice, who has 1,000 €

        Assert.Null(model.AmountError);
        Assert.Equal((InMemoryBank.Alice, InMemoryBank.Bob), (model.Source!.Value, model.Target!.Value));
    }

    [Fact]
    public void The_same_account_on_both_sides_is_refused()
    {
        var model = Apply(Loaded, new SourceChosen(0), new TargetChosen(0), new AmountTyped("10"));

        Assert.Equal("Choose two different accounts.", model.AccountsError);
        Assert.False(model.CanTransfer);
    }

    [Fact]
    public void A_click_sends_the_transfer_and_a_second_click_sends_nothing()
    {
        var (sending, effects) = BankScreen.Update(new TransferClicked(), ReadyToTransfer("250"));
        var (_, secondClick) = BankScreen.Update(new TransferClicked(), sending);

        Assert.Equal([new SendTransfer(InMemoryBank.Alice, InMemoryBank.Bob, 250m)], effects);
        Assert.True(sending.Busy);
        Assert.Empty(secondClick);
    }

    [Fact]
    public void From_1000_euros_a_question_is_asked_first_and_nothing_is_sent()
    {
        var model = Apply(Loaded, new SourceChosen(1), new TargetChosen(2), new AmountTyped("1000"));

        var (asking, effects) = BankScreen.Update(new TransferClicked(), model);

        Assert.Empty(effects);
        Assert.Equal("Transfer 1,000.00 € from Alice — Savings account to Bob — Current account?", asking.PendingConfirmation);
        Assert.False(asking.CanTransfer);
    }

    [Fact]
    public void Answering_no_cancels_answering_yes_sends()
    {
        var asking = Apply(Loaded, new SourceChosen(1), new TargetChosen(2), new AmountTyped("1000"), new TransferClicked());

        var (cancelled, noEffects) = BankScreen.Update(new ConfirmationAnswered(false), asking);
        var (confirmed, effects) = BankScreen.Update(new ConfirmationAnswered(true), asking);

        Assert.Equal((null, "Transfer cancelled."), (cancelled.PendingConfirmation, cancelled.Notice!.Text));
        Assert.Empty(noEffects);
        Assert.Equal([new SendTransfer(InMemoryBank.AliceSavings, InMemoryBank.Bob, 1000m)], effects);
        Assert.True(confirmed.Busy);
    }

    [Fact]
    public void A_transfer_done_empties_the_amount_and_reloads_the_balances_keeping_the_selection()
    {
        var sending = Apply(ReadyToTransfer("250"), new AccountSelected(2), new TransferClicked());   // Bob's history on screen

        var (done, effects) = BankScreen.Update(new TransferDone(new Transfer(InMemoryBank.Alice, InMemoryBank.Bob, 250m, 750m, 750m)), sending);
        var (reloaded, reloadEffects) = BankScreen.Update(
            new AccountsLoaded([AliceCurrent with { Balance = 750m }, AliceSavings, Bob with { Balance = 750m }]), done);

        Assert.Equal(("", false), (done.AmountText, done.Busy));
        Assert.Equal("Transfer of 250.00 € done. New balance: 750.00 €.", done.Notice!.Text);
        Assert.Equal([new LoadAccounts()], effects);
        Assert.Equal(InMemoryBank.Bob, reloaded.Selected);
        Assert.Equal([new LoadOperations(InMemoryBank.Bob, reloaded.OperationsRequest)], reloadEffects);
    }

    [Fact]
    public void A_refusal_by_the_bank_is_shown_and_the_button_comes_back()
    {
        var sending = Apply(ReadyToTransfer("250"), new TransferClicked());

        var failed = Apply(sending, new TransferFailed("Insufficient balance: 100.00 € available."));

        Assert.Equal(new Notice("Insufficient balance: 100.00 € available.", IsError: true), failed.Notice);
        Assert.True(failed.CanTransfer);
    }
}
