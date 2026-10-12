using System.ComponentModel.DataAnnotations;
using Ui.Bank;

namespace Ui.Mvc.Models;

// The data the controller passes to each view. In ASP.NET Core vocabulary, they are often called
// "view models": they are NOT the ViewModels of MVVM (which have a state, commands and live with the view),
// just bags of data prepared for a rendering, then thrown away.

public sealed record AccountLine(Guid Id, string Owner, string Label, string Balance);

public sealed record AccountList(IReadOnlyList<AccountLine> Accounts);

public sealed record OperationLine(string Date, string Label, string Amount, bool IsDebit);

public sealed record AccountDetail(AccountLine Account, IReadOnlyList<OperationLine> Operations);

/// <summary>
/// The transfer form, both ways: filled by model binding from the fields sent,
/// sent back to the view (with the errors) when it must be displayed again.
/// </summary>
public sealed class TransferForm : IValidatableObject
{
    [Required(ErrorMessage = "Choose the account to debit.")]
    [Display(Name = "Account to debit")]
    public Guid? Source { get; set; }

    [Required(ErrorMessage = "Choose the account to credit.")]
    [Display(Name = "Account to credit")]
    public Guid? Target { get; set; }

    [Required(ErrorMessage = "Enter an amount.")]
    [Display(Name = "Amount (€)")]
    public decimal? Amount { get; set; }

    /// <summary>True when the user has validated the confirmation screen (large amounts).</summary>
    public bool Confirmed { get; set; }

    /// <summary>The accounts offered in the lists: not sent by the browser, reloaded by the controller.</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public IReadOnlyList<AccountLine> Accounts { get; set; } = [];

    // Rules that span several fields: called by the MVC validation, after the attributes.
    // They are the same as the bank's, to warn BEFORE the call; the bank checks them again.
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Amount is { } amount && TransferRules.CheckAmount(amount) is { } amountError)
            yield return new ValidationResult(amountError, [nameof(Amount)]);
        if (Source is { } source && Target is { } target && TransferRules.CheckAccounts(source, target) is { } accountsError)
            yield return new ValidationResult(accountsError, [nameof(Target)]);
    }
}
