using System.ComponentModel;
using Ui.Bank;
using Ui.Presentation;

namespace Ui.Tests;

/// <summary>
/// The complete behaviour of the screen, without a screen: what the user would see is read in the
/// ViewModel's properties, and what they would do is written into them. That is the promise of MVVM.
/// </summary>
public class ViewModelTests
{
    private readonly RemoteControlledBank _bank = new();
    private readonly ProgrammedConfirmation _confirmation = new(answer: true);
    private readonly MainScreenViewModel _screen;

    public ViewModelTests() => _screen = new MainScreenViewModel(_bank, _confirmation);

    private TransferViewModel Transfer => _screen.Transfer;

    private AccountViewModel Account(Guid id) => _screen.Accounts.Accounts.Single(c => c.Id == id);

    private async Task Prepare(Guid source, Guid target, string amount)
    {
        await _screen.InitializeAsync();
        Transfer.Source = Account(source);
        Transfer.Target = Account(target);
        Transfer.AmountText = amount;
    }

    [Fact]
    public async Task At_startup_the_accounts_are_loaded_and_the_first_one_selected()
    {
        await _screen.InitializeAsync();

        Assert.Equal(["Alice — Current account", "Alice — Savings account", "Bob — Current account"], _screen.Accounts.Accounts.Select(c => c.Display));
        Assert.Equal("1,000.00 €", _screen.Accounts.Accounts[0].BalanceDisplay);
        Assert.Same(_screen.Accounts.Accounts[0], _screen.Accounts.SelectedAccount);
        Assert.Equal("Operations — Alice — Current account", _screen.Accounts.OperationsTitle);
        Assert.Equal("+1,000.00 €", Assert.Single(_screen.Accounts.Operations).Amount);
        Assert.False(Transfer.TransferCommand.CanExecute(null));   // empty form: button greyed out
    }

    [Fact]
    public async Task Each_keystroke_is_checked_and_the_view_is_told()
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Bob, "");
        var properties = new List<string?>();
        var errors = new List<string?>();
        Transfer.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        Transfer.ErrorsChanged += (_, e) => errors.Add(e.PropertyName);

        Transfer.AmountText = "abc";

        // What a WPF binding listens to: the input, the error text, the error state; and the red border (ErrorsChanged).
        Assert.Equal(["AmountText", "AmountError", "HasErrors"], properties);
        Assert.Equal(["AmountText"], errors);
        Assert.Equal("Enter an amount, for example 250.50.", Transfer.AmountError);
        Assert.Equal(["Enter an amount, for example 250.50."], Transfer.GetErrors("AmountText").Cast<string>());
        Assert.Equal("abc", Transfer.AmountText);   // what was typed stays displayed

        properties.Clear();
        Transfer.AmountText = "abcd";               // same error: nothing to redraw
        Assert.Equal(["AmountText"], properties);

        properties.Clear();
        Transfer.AmountText = "abcd";               // same value (WPF often rewrites the displayed value): no signal
        Assert.Empty(properties);
    }

    [Fact]
    public async Task The_Transfer_button_follows_the_input()
    {
        await _screen.InitializeAsync();
        var states = new List<bool>();
        Transfer.TransferCommand.CanExecuteChanged += (_, _) => states.Add(Transfer.TransferCommand.CanExecute(null));

        Transfer.Source = Account(InMemoryBank.Alice);
        Transfer.Target = Account(InMemoryBank.Bob);
        Transfer.AmountText = "250";
        Transfer.AmountText = "2500";
        Transfer.AmountText = "25";

        // Told at each change; enabled only when source, target and amount are there and right.
        Assert.Equal([false, false, true, false, true], states);
    }

    [Theory]
    [InlineData("250", "250")]
    [InlineData("250.5", "250.5")]
    [InlineData("250.50", "250.50")]
    [InlineData("1,000", "1000")]
    [InlineData(" 1,000.00 € ", "1000.00")]
    public async Task Amounts_are_typed_the_usual_way(string input, string expected)
    {
        await Prepare(InMemoryBank.AliceSavings, InMemoryBank.Bob, input);

        Assert.Null(Transfer.AmountError);
        Assert.True(Transfer.TransferCommand.CanExecute(null));
        await Transfer.TransferCommand.ExecuteAsync();
        Assert.Equal(8_000m - decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Account(InMemoryBank.AliceSavings).Balance);
    }

    [Theory]
    [InlineData("-5", "The amount must be positive.")]
    [InlineData("0", "The amount must be positive.")]
    [InlineData("12.345", "The amount is given to the cent.")]
    [InlineData("1,000.01", "Insufficient balance: 1,000.00 € available.")]
    [InlineData("12 abc", "Enter an amount, for example 250.50.")]
    public async Task Errors_are_displayed_even_before_sending(string input, string error)
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Bob, input);

        Assert.Equal(error, Transfer.AmountError);
        Assert.False(Transfer.TransferCommand.CanExecute(null));
        Assert.Equal(0, _bank.TransfersRequested);
    }

    [Fact]
    public async Task The_same_account_twice_is_reported_and_Swap_inverts_the_accounts()
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Alice, "10");
        Assert.Equal("Choose two different accounts.", Transfer.TargetError);

        Transfer.Target = Account(InMemoryBank.Bob);
        Assert.Null(Transfer.TargetError);
        Transfer.SwapCommand.Execute(null);

        Assert.Equal((InMemoryBank.Bob, InMemoryBank.Alice), (Transfer.Source!.Id, Transfer.Target!.Id));
    }

    [Fact]
    public async Task The_source_balance_is_followed_even_when_it_changes_elsewhere()
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Bob, "900");
        Assert.Null(Transfer.AmountError);

        // A transfer made elsewhere (another window, another device), then a refresh of the list.
        await _bank.Real.TransferAsync(InMemoryBank.Alice, InMemoryBank.AliceSavings, 200m);
        await _screen.Accounts.RefreshAsync();

        Assert.Equal("Insufficient balance: 800.00 € available.", Transfer.AmountError);

        // Once the source changed, the former one is no longer followed (otherwise: leak and ghost errors).
        Transfer.Source = Account(InMemoryBank.AliceSavings);
        var changes = 0;
        Transfer.PropertyChanged += (_, _) => changes++;
        Transfer.TransferCommand.CanExecuteChanged += (_, _) => changes++;
        await _bank.Real.TransferAsync(InMemoryBank.Alice, InMemoryBank.Bob, 100m);
        await _screen.Accounts.RefreshAsync();
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task During_the_transfer_the_screen_waits_and_a_double_click_does_not_transfer_twice()
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Bob, "250.50");
        var aliceDisplayed = Account(InMemoryBank.Alice);
        _bank.Hold("Transfer");

        var click = Transfer.TransferCommand.ExecuteAsync();
        Assert.True(Transfer.IsBusy);
        Assert.Equal("Transfer in progress…", Transfer.Message);
        Assert.False(Transfer.TransferCommand.CanExecute(null));
        Transfer.TransferCommand.Execute(null);                 // second click, button greyed out: ignored

        _bank.Release("Transfer");
        await click;

        Assert.Equal(1, _bank.TransfersRequested);
        Assert.False(Transfer.IsBusy);
        Assert.Equal("Transfer of 250.50 € done. New balance: 749.50 €.", Transfer.Message);
        Assert.False(Transfer.MessageIsError);
        Assert.Equal("", Transfer.AmountText);
        // Balances refreshed IN PLACE: the same object, hence the selection and the lists do not move.
        Assert.Same(aliceDisplayed, Account(InMemoryBank.Alice));
        Assert.Equal("749.50 €", aliceDisplayed.BalanceDisplay);
        Assert.Equal("-250.50 €", _screen.Accounts.Operations[0].Amount);   // the displayed history too
    }

    [Fact]
    public async Task A_refusal_of_the_bank_is_displayed_as_an_error()
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Bob, "900");
        // Between the input and the click, the account was emptied elsewhere, and the screen does not know it yet.
        await _bank.Real.TransferAsync(InMemoryBank.Alice, InMemoryBank.AliceSavings, 500m);

        await Transfer.TransferCommand.ExecuteAsync();

        Assert.Equal("Insufficient balance: 500.00 € available.", Transfer.Message);
        Assert.True(Transfer.MessageIsError);
        Assert.False(Transfer.IsBusy);
        Assert.Equal("900", Transfer.AmountText);              // the input is kept
        Assert.True(Transfer.TransferCommand.CanExecute(null));    // and the button enabled again
    }

    [Theory]
    [InlineData(false, "Transfer cancelled.", 8_000)]
    [InlineData(true, "Transfer of 1,500.00 € done. New balance: 6,500.00 €.", 6_500)]
    public async Task A_large_amount_asks_for_confirmation(bool answer, string message, int savingsBalance)
    {
        var confirmation = new ProgrammedConfirmation(answer);
        var screen = new MainScreenViewModel(_bank, confirmation);
        await screen.InitializeAsync();
        screen.Transfer.Source = screen.Accounts.Accounts[1];   // Savings account
        screen.Transfer.Target = screen.Accounts.Accounts[2];   // Bob
        screen.Transfer.AmountText = "1500";

        await screen.Transfer.TransferCommand.ExecuteAsync();

        Assert.Equal("Transfer 1,500.00 € from Alice — Savings account to Bob — Current account?", Assert.Single(confirmation.Questions));
        Assert.Equal(message, screen.Transfer.Message);
        Assert.Equal(savingsBalance, (await _bank.Real.AccountAsync(InMemoryBank.AliceSavings)).Balance);
    }

    [Fact]
    public async Task A_small_amount_asks_nothing()
    {
        await Prepare(InMemoryBank.Alice, InMemoryBank.Bob, "999.99");

        await Transfer.TransferCommand.ExecuteAsync();

        Assert.Empty(_confirmation.Questions);
        Assert.Equal(1, _bank.TransfersRequested);
    }

    [Fact]
    public async Task A_response_that_arrives_too_late_does_not_overwrite_the_next_selection()
    {
        await _screen.InitializeAsync();
        var accounts = _screen.Accounts;

        // Click on Bob, then right away on the Savings account; both responses are late.
        _bank.Hold("Operations", InMemoryBank.Bob);
        _bank.Hold("Operations", InMemoryBank.AliceSavings);
        accounts.SelectedAccount = Account(InMemoryBank.Bob);
        var bobLoading = accounts.OperationsLoading;
        accounts.SelectedAccount = Account(InMemoryBank.AliceSavings);
        var savingsLoading = accounts.OperationsLoading;

        // The response for Bob arrives first, but it is stale: ignored, and the Savings account is still awaited.
        _bank.Release("Operations", InMemoryBank.Bob);
        await bobLoading;
        Assert.Empty(accounts.Operations);
        Assert.True(accounts.LoadingInProgress);

        _bank.Release("Operations", InMemoryBank.AliceSavings);
        await savingsLoading;

        Assert.Equal("Operations — Alice — Savings account", accounts.OperationsTitle);
        Assert.Equal("+8,000.00 €", Assert.Single(accounts.Operations).Amount);
        Assert.False(accounts.LoadingInProgress);
    }
}
