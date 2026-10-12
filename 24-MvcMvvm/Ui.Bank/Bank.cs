namespace Ui.Bank;

public sealed record Account(Guid Id, string Owner, string Label, decimal Balance);

public sealed record Operation(DateTimeOffset Date, string Label, decimal Amount);

public sealed record Transfer(Guid Source, Guid Target, decimal Amount, decimal SourceBalance, decimal TargetBalance);

/// <summary>A refused business rule. The message is made to be shown as is to the user.</summary>
public sealed class BusinessError(string message) : Exception(message);

/// <summary>
/// What the bank offers to the screens. Asynchronous, like a real remote service: that is what forces an interface
/// to handle the waiting (button disabled, activity indicator, responses arriving out of order).
/// </summary>
public interface IBank
{
    Task<IReadOnlyList<Account>> AccountsAsync(CancellationToken cancellation = default);

    Task<Account> AccountAsync(Guid id, CancellationToken cancellation = default);

    /// <summary>The operations of the account, from the most recent to the oldest.</summary>
    Task<IReadOnlyList<Operation>> OperationsAsync(Guid account, CancellationToken cancellation = default);

    Task<Transfer> TransferAsync(Guid source, Guid target, decimal amount, CancellationToken cancellation = default);
}

/// <summary>
/// The rules of a transfer, public so that the interface can warn the user BEFORE sending
/// (the ViewModel calls them at each keystroke). The bank applies them again, and it is the authority:
/// between the input and the sending, the balance may have changed.
/// </summary>
public static class TransferRules
{
    /// <summary>Beyond it, the interface asks for a confirmation (an ergonomics rule, not a bank one).</summary>
    public const decimal ConfirmationThreshold = 1_000m;

    public static string? CheckAmount(decimal amount)
    {
        if (amount <= 0)
            return "The amount must be positive.";
        if (decimal.Round(amount, 2) != amount)
            return "The amount is given to the cent.";
        return null;
    }

    public static string? CheckAccounts(Guid source, Guid target) =>
        source == target ? "Choose two different accounts." : null;

    public static string? CheckBalance(decimal balance, decimal amount) =>
        amount > balance ? $"Insufficient balance: {Format.Euros(balance)} available." : null;
}

/// <summary>The format of the amounts, the same in all the screens (and in the error messages).</summary>
public static class Format
{
    public static readonly System.Globalization.CultureInfo British = System.Globalization.CultureInfo.GetCultureInfo("en-GB");

    // A fixed culture: the amounts look the same whatever the machine's settings,
    // which makes them easy to read in a console, to type and to look for in a test.
    public static string Euros(decimal amount) =>
        amount.ToString("#,##0.00 €", British);

    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    /// <summary>A date, in Paris time whatever the time zone in which it was recorded.</summary>
    public static string Date(DateTimeOffset date) =>
        TimeZoneInfo.ConvertTime(date, Paris).ToString("dd/MM/yyyy HH:mm", British);
}
