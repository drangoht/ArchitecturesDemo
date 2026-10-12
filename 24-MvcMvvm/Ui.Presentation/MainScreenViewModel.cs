using Ui.Bank;

namespace Ui.Presentation;

/// <summary>
/// The window's ViewModel: it assembles the two parts of the screen and links them (a successful transfer refreshes
/// the balances). The view only has to take it as DataContext; it contains no code.
/// </summary>
public sealed class MainScreenViewModel
{
    public MainScreenViewModel(IBank bank, IConfirmation confirmation)
    {
        Accounts = new AccountsViewModel(bank);
        Transfer = new TransferViewModel(bank, confirmation, Accounts.Accounts, Accounts.RefreshAsync);
    }

    public string Title => "MVVM Bank";

    public AccountsViewModel Accounts { get; }

    public TransferViewModel Transfer { get; }

    public Task InitializeAsync() => Accounts.RefreshAsync();
}
