using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Ui.Bank;

namespace Ui.Presentation;

/// <summary>
/// The transfer form, alive: each keystroke updates the ViewModel (two-way binding), which
/// checks everything again and signals the view. The errors appear while typing, the button is enabled when everything
/// is right, and the source's balance is followed: if it changes, the "insufficient balance" error too.
/// Compare with the MVC controller, which only sees the form once sent.
/// </summary>
public sealed class TransferViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly IBank _bank;
    private readonly IConfirmation _confirmation;
    private readonly Func<Task> _afterTransfer;
    private readonly Dictionary<string, string> _errors = [];

    private AccountViewModel? _source;
    private AccountViewModel? _target;
    private string _amountText = "";
    private decimal? _amount;
    private string? _message;
    private bool _messageIsError;
    private bool _isBusy;

    public TransferViewModel(IBank bank, IConfirmation confirmation, ObservableCollection<AccountViewModel> accounts, Func<Task> afterTransfer)
    {
        _bank = bank;
        _confirmation = confirmation;
        _afterTransfer = afterTransfer;
        Accounts = accounts;
        TransferCommand = new AsyncCommand(TransferAsync, () => CanTransfer);
        SwapCommand = new Command(Swap, () => Source is not null || Target is not null);
    }

    /// <summary>The same objects as the list of accounts: a balance updated is updated everywhere.</summary>
    public ObservableCollection<AccountViewModel> Accounts { get; }

    public AsyncCommand TransferCommand { get; }

    public Command SwapCommand { get; }

    public AccountViewModel? Source
    {
        get => _source;
        set
        {
            var former = _source;
            if (!Set(ref _source, value))
                return;
            if (former is not null)
                former.PropertyChanged -= SourceBalanceChanged;
            if (value is not null)
                value.PropertyChanged += SourceBalanceChanged;
            Validate();
        }
    }

    public AccountViewModel? Target
    {
        get => _target;
        set
        {
            if (Set(ref _target, value))
                Validate();
        }
    }

    /// <summary>The text as typed: "abc" must stay displayed (with an error), not be replaced by 0.</summary>
    public string AmountText
    {
        get => _amountText;
        set
        {
            if (Set(ref _amountText, value ?? ""))
            {
                _amount = Parse(_amountText);
                Validate();
            }
        }
    }

    public string? AmountError => _errors.GetValueOrDefault(nameof(AmountText));

    public string? TargetError => _errors.GetValueOrDefault(nameof(Target));

    public string? Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public bool MessageIsError
    {
        get => _messageIsError;
        private set => Set(ref _messageIsError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    private bool CanTransfer => _source is not null && _target is not null && _amount is not null && !HasErrors;

    // ─── INotifyDataErrorInfo: WPF surrounds in red the fields bound to the properties in error ───

    public bool HasErrors => _errors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? property) =>
        property is not null && _errors.TryGetValue(property, out var error) ? new[] { error } : Array.Empty<string>();

    private void Validate()
    {
        string? amountError = null;
        if (_amountText.Trim().Length > 0)
        {
            amountError = _amount is not { } amount
                ? "Enter an amount, for example 250.50."
                : TransferRules.CheckAmount(amount)
                  ?? (_source is null ? null : TransferRules.CheckBalance(_source.Balance, amount));
        }
        var targetError = _source is not null && _target is not null ? TransferRules.CheckAccounts(_source.Id, _target.Id) : null;

        var before = HasErrors;
        Record(nameof(AmountText), amountError, nameof(AmountError));
        Record(nameof(Target), targetError, nameof(TargetError));
        if (before != HasErrors)
            Notify(nameof(HasErrors));
        TransferCommand.Reevaluate();
        SwapCommand.Reevaluate();
    }

    private void Record(string property, string? error, string textProperty)
    {
        if (_errors.GetValueOrDefault(property) == error)
            return;
        if (error is null)
            _errors.Remove(property);
        else
            _errors[property] = error;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
        Notify(textProperty);
    }

    private void SourceBalanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.Balance))
            Validate();
    }

    // ─── Commands ───

    private async Task TransferAsync()
    {
        var (source, target, amount) = (_source!, _target!, _amount!.Value);

        if (amount >= TransferRules.ConfirmationThreshold
            && !await _confirmation.AskAsync($"Transfer {Format.Euros(amount)} from {source.Display} to {target.Display}?"))
        {
            Inform("Transfer cancelled.", error: false);
            return;
        }

        IsBusy = true;
        Inform("Transfer in progress…", error: false);
        try
        {
            var transfer = await _bank.TransferAsync(source.Id, target.Id, amount);
            AmountText = "";
            Inform($"Transfer of {Format.Euros(transfer.Amount)} done. New balance: {Format.Euros(transfer.SourceBalance)}.", error: false);
            await _afterTransfer();
        }
        catch (BusinessError error)
        {
            // The bank is the authority: between the input and the sending, the balance may have changed elsewhere.
            Inform(error.Message, error: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Swap() => (Source, Target) = (Target, Source);

    private void Inform(string message, bool error)
    {
        MessageIsError = error;
        Message = message;
    }

    /// <summary>"250", "250.5", "250.50", "1,000 €": everything a user may type (spaces and thousands separators ignored).</summary>
    private static decimal? Parse(string text)
    {
        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '€' && c != ',').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }
}
