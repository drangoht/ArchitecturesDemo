using System.Collections.ObjectModel;
using Ui.Bank;

namespace Ui.Presentation;

/// <summary>An account as the screen shows it. Its balance changes in place after a transfer: the view follows.</summary>
public sealed class AccountViewModel(Account account) : ObservableObject
{
    private decimal _balance = account.Balance;

    public Guid Id { get; } = account.Id;
    public string Owner { get; } = account.Owner;
    public string Label { get; } = account.Label;
    public string Display => $"{Owner} — {Label}";

    public decimal Balance
    {
        get => _balance;
        internal set
        {
            if (Set(ref _balance, value))
                Notify(nameof(BalanceDisplay));
        }
    }

    /// <summary>Formatted here rather than in each view: they all display it the same way, and a test can check it.</summary>
    public string BalanceDisplay => Format.Euros(Balance);

    public override string ToString() => Display;
}

/// <summary>A history line: nothing changes, so nothing to signal.</summary>
public sealed class OperationViewModel(Operation operation)
{
    public string Date { get; } = Format.Date(operation.Date);
    public string Label { get; } = operation.Label;
    public string Amount { get; } = (operation.Amount > 0 ? "+" : "") + Format.Euros(operation.Amount);
    public bool IsDebit { get; } = operation.Amount < 0;
}

/// <summary>
/// The list of accounts and the history of the selected account. Selecting an account (click in the list, SelectedItem
/// binding) starts loading its operations; nothing tells where the selection comes from, and that is intended.
/// </summary>
public sealed class AccountsViewModel(IBank bank) : ObservableObject
{
    private AccountViewModel? _selection;
    private bool _loadingInProgress;
    private string? _error;
    private int _lastRequest;

    /// <summary>An ObservableCollection signals additions and removals: the list on screen follows them.</summary>
    public ObservableCollection<AccountViewModel> Accounts { get; } = [];

    public ObservableCollection<OperationViewModel> Operations { get; } = [];

    public AccountViewModel? SelectedAccount
    {
        get => _selection;
        set
        {
            if (Set(ref _selection, value))
            {
                Notify(nameof(OperationsTitle));
                OperationsLoading = LoadOperationsAsync(value);
            }
        }
    }

    public string OperationsTitle => _selection is null ? "Operations" : $"Operations — {_selection.Display}";

    /// <summary>The last loading started: the view ignores it, the tests await it.</summary>
    public Task OperationsLoading { get; private set; } = Task.CompletedTask;

    public bool LoadingInProgress
    {
        get => _loadingInProgress;
        private set => Set(ref _loadingInProgress, value);
    }

    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    /// <summary>Reloads the balances (updating the accounts already displayed) and the history of the selected account.</summary>
    public async Task RefreshAsync()
    {
        foreach (var account in await bank.AccountsAsync())
        {
            if (Accounts.FirstOrDefault(c => c.Id == account.Id) is { } displayed)
                displayed.Balance = account.Balance;   // the same object: the selection and the drop-down lists stay in place
            else
                Accounts.Add(new AccountViewModel(account));
        }

        if (SelectedAccount is null)
            SelectedAccount = Accounts.FirstOrDefault();
        else
            OperationsLoading = LoadOperationsAsync(SelectedAccount);
        await OperationsLoading;
    }

    private async Task LoadOperationsAsync(AccountViewModel? account)
    {
        // Clicking on A then on B: if the response for A arrives AFTER the one for B, it must not be displayed
        // under B's title. Each loading has a number; only the response of the last one is kept.
        var request = ++_lastRequest;
        Operations.Clear();
        Error = null;
        if (account is null)
            return;

        LoadingInProgress = true;
        try
        {
            var operations = await bank.OperationsAsync(account.Id);
            if (request != _lastRequest)
                return;
            foreach (var operation in operations)
                Operations.Add(new OperationViewModel(operation));
        }
        catch (BusinessError error)
        {
            if (request == _lastRequest)
                Error = error.Message;
        }
        finally
        {
            if (request == _lastRequest)
                LoadingInProgress = false;
        }
    }
}
