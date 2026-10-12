using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Ui.Presentation;

// What CommunityToolkit.Mvvm or Prism would bring (ObservableObject, RelayCommand, AsyncRelayCommand),
// written by hand: it is short, and one can see there is no magic.

/// <summary>The base of the ViewModels: signal to the views when a property changes.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Changes the field and signals, only if the value really changes (otherwise the views redraw for nothing).</summary>
    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;
        storage = value;
        Notify(property!);
        return true;
    }

    /// <summary>Signals that a computed property (without a field of its own) may have changed.</summary>
    protected void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>A command that can be awaited: the console view awaits it, the tests too.</summary>
public interface IAsyncCommand : ICommand
{
    Task ExecuteAsync();
}

/// <summary>
/// A command bound to a button. CanExecute decides whether the button is enabled; when the answer may have changed,
/// the ViewModel calls <see cref="Reevaluate"/> and WPF queries CanExecute again.
/// </summary>
public sealed class Command(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            execute();
    }

    public void Reevaluate() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// An asynchronous command: disabled DURING its execution, which settles the double click (a second click during
/// the call to the bank does not redo the transfer).
/// </summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : IAsyncCommand
{
    private bool _inProgress;

    public event EventHandler? CanExecuteChanged;

    public bool InProgress => _inProgress;

    public bool CanExecute(object? parameter) => !_inProgress && (canExecute?.Invoke() ?? true);

    // ICommand.Execute returns nothing: it is the only accepted "async void" (event handler).
    // An unexpected exception goes up to the application's global handler; the expected errors
    // (refusal of the bank) are handled by the ViewModel itself.
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
            return;
        _inProgress = true;
        Reevaluate();
        try
        {
            await execute();
        }
        finally
        {
            _inProgress = false;
            Reevaluate();
        }
    }

    public void Reevaluate() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// What a ViewModel needs from the interface without knowing it: asking a question. WPF answers with a dialog
/// box, the console with a line read at the keyboard, the tests with a programmed answer. A ViewModel that
/// would call MessageBox.Show would no longer be testable.
/// </summary>
public interface IConfirmation
{
    Task<bool> AskAsync(string question);
}
