using Ui.Bank;
using Ui.Presentation;

namespace Ui.Console;

/// <summary>
/// The console view. Like the WPF window, it only does two things: write into the ViewModel what the user
/// types, and show what the ViewModel signals to it. The "⚠", "»" and "[Transfer]" lines appear
/// by themselves, pushed by the bindings when the ViewModel changes: the view did not ask for them.
/// </summary>
public sealed class ConsoleView : IDisposable
{
    private readonly MainScreenViewModel _screen;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly List<Binding> _bindings = [];
    private bool _transferEnabled;

    public ConsoleView(MainScreenViewModel screen, TextReader input, TextWriter output)
    {
        _screen = screen;
        _input = input;
        _output = output;
        var transfer = screen.Transfer;

        // The bindings of this view (in WPF: the {Binding …} of the XAML). No initial value: nothing to say at the start.
        _bindings.Add(Binding.Bind(transfer, nameof(TransferViewModel.AmountError), v => Signal("⚠ amount", v), pushNow: false));
        _bindings.Add(Binding.Bind(transfer, nameof(TransferViewModel.TargetError), v => Signal("⚠ accounts", v), pushNow: false));
        _bindings.Add(Binding.Bind(transfer, nameof(TransferViewModel.Message), v => Signal("»", v), pushNow: false));

        // Command="{Binding TransferCommand}": the button follows CanExecuteChanged to grey out or not.
        transfer.TransferCommand.CanExecuteChanged += (_, _) =>
        {
            var enabled = transfer.TransferCommand.CanExecute(null);
            if (enabled != _transferEnabled)
                _output.WriteLine($"  [Transfer] {(enabled ? "enabled" : "greyed out")}");
            _transferEnabled = enabled;
        };
    }

    public async Task RunAsync()
    {
        _output.WriteLine($"{_screen.Title}, console view. Type \"help\" for the list of commands.");
        await _screen.InitializeAsync();
        ShowAccounts();

        while (await _input.ReadLineAsync() is { } line)
        {
            var words = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                continue;
            _output.WriteLine($"> {line.Trim()}");
            var argument = words.Length > 1 ? words[1] : "";
            var accounts = _screen.Accounts.Accounts;
            switch (words[0].ToLowerInvariant())
            {
                case "accounts":
                    ShowAccounts();
                    break;
                case "show" when Account(argument) is { } account:
                    _screen.Accounts.SelectedAccount = account;
                    await _screen.Accounts.OperationsLoading;
                    ShowOperations();
                    break;
                case "from" when Account(argument) is { } account:
                    _screen.Transfer.Source = account;
                    break;
                case "to" when Account(argument) is { } account:
                    _screen.Transfer.Target = account;
                    break;
                case "amount":
                    _screen.Transfer.AmountText = argument;
                    break;
                case "swap":
                    _screen.Transfer.SwapCommand.Execute(null);
                    _output.WriteLine($"  from: {_screen.Transfer.Source?.Display ?? "—"}; to: {_screen.Transfer.Target?.Display ?? "—"}");
                    break;
                case "transfer":
                    // Like a click: a greyed-out button does nothing.
                    if (!_screen.Transfer.TransferCommand.CanExecute(null))
                        _output.WriteLine("  (the Transfer button is greyed out)");
                    else
                        await _screen.Transfer.TransferCommand.ExecuteAsync();
                    break;
                case "quit" or "q":
                    return;
                case "help":
                    _output.WriteLine("  accounts | show N | from N | to N | amount 250.50 | swap | transfer | quit   (N: number of the account)");
                    break;
                default:
                    _output.WriteLine("  Unknown command (type \"help\").");
                    break;
            }

            AccountViewModel? Account(string text) =>
                int.TryParse(text, out var n) && n >= 1 && n <= accounts.Count ? accounts[n - 1] : null;
        }
    }

    private void Signal(string prefix, object? value)
    {
        if (value is string text && text.Length > 0)
            _output.WriteLine($"  {prefix} {text}");
    }

    private void ShowAccounts()
    {
        var accounts = _screen.Accounts.Accounts;
        for (var i = 0; i < accounts.Count; i++)
            _output.WriteLine($"  {i + 1}. {accounts[i].Display,-24} {accounts[i].BalanceDisplay,14}");
    }

    private void ShowOperations()
    {
        _output.WriteLine($"  {_screen.Accounts.OperationsTitle}");
        foreach (var operation in _screen.Accounts.Operations)
            _output.WriteLine($"    {operation.Date}  {operation.Label,-40} {operation.Amount,14}");
    }

    public void Dispose()
    {
        foreach (var binding in _bindings)
            binding.Dispose();
    }
}

/// <summary>The ViewModel's question, asked at the keyboard.</summary>
public sealed class ConsoleConfirmation(TextReader input, TextWriter output) : IConfirmation
{
    public async Task<bool> AskAsync(string question)
    {
        output.WriteLine($"  ? {question} (y/n)");
        var answer = await input.ReadLineAsync();
        output.WriteLine($"> {answer}");
        return answer?.Trim().ToLowerInvariant() is "y" or "yes";
    }
}
