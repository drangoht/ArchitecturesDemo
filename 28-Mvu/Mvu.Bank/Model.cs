using System.Collections.Immutable;
using System.Globalization;
using Ui.Bank;

namespace Mvu.Bank;

/// <summary>
/// THE WHOLE STATE OF THE SCREEN, in one immutable value: what is selected, typed, loading, waiting for confirmation.
/// Nothing else holds state. Every message produces a new Model (a copy with `with`), never a change in place.
/// </summary>
public sealed record Model(
    ImmutableList<Account> Accounts,
    Guid? Selected,
    ImmutableList<Operation> Operations,
    int OperationsRequest,
    bool LoadingOperations,
    Guid? Source,
    Guid? Target,
    string AmountText,
    bool Busy,
    string? PendingConfirmation,
    Notice? Notice)
{
    public static Model Empty { get; } = new([], null, [], 0, false, null, null, "", false, null, null);

    // ─── Everything else is DERIVED from the state, recomputed each time: no Validate() to call, nothing to forget ───

    public Account? SourceAccount => Accounts.Find(a => a.Id == Source);

    public decimal? Amount => ParseAmount(AmountText);

    public string? AmountError =>
        AmountText.Trim().Length == 0 ? null
        : Amount is not { } amount ? "Enter an amount, for example 250.50."
        : TransferRules.CheckAmount(amount) ?? (SourceAccount is { } source ? TransferRules.CheckBalance(source.Balance, amount) : null);

    public string? AccountsError =>
        Source is { } source && Target is { } target ? TransferRules.CheckAccounts(source, target) : null;

    public bool CanTransfer =>
        Source is not null && Target is not null && Amount is not null && AmountError is null && AccountsError is null
        && !Busy && PendingConfirmation is null;

    /// <summary>"250", "250.5", "1,000 €": spaces, euro sign and thousands separators ignored.</summary>
    public static decimal? ParseAmount(string text)
    {
        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '€' && c != ',').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }
}

public sealed record Notice(string Text, bool IsError);

public static class AccountText
{
    public static string Display(Account account) => $"{account.Owner} — {account.Label}";
}
