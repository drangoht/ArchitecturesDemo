using Mvu.Runtime;
using Ui.Bank;
using H = Mvu.Runtime.Elements<Mvu.Bank.Msg>;

namespace Mvu.Bank;

/// <summary>
/// The view: a pure function from the model to a description of the screen. It reads the model and says which
/// message each element sends; it never changes anything. Greying out the button = giving it no message.
/// </summary>
public static class BankView
{
    public static Node<Msg> View(Model model)
    {
        var accounts = model.Accounts.Select(a => $"{AccountText.Display(a)} {Format.Euros(a.Balance)}").ToList();
        int? IndexOf(Guid? id) => model.Accounts.FindIndex(a => a.Id == id) is var i and >= 0 ? i : null;
        var selected = model.Accounts.Find(a => a.Id == model.Selected);

        return H.Column(
            H.Text("MVU Bank", Tone.Title),
            H.Choice("accounts", "Accounts", accounts, IndexOf(model.Selected), i => new AccountSelected(i)),
            H.Text(selected is null ? "Operations" : $"Operations — {AccountText.Display(selected)}", Tone.Title),
            model.LoadingOperations
                ? H.Text("loading…", Tone.Muted)
                : H.Column(model.Operations.Take(5).Select(o =>
                    H.Text($"{Format.Date(o.Date)}  {o.Label,-40} {(o.Amount > 0 ? "+" : "")}{Format.Euros(o.Amount)}", Tone.Muted))),
            H.Text("Transfer", Tone.Title),
            H.Choice("from", "From", accounts, IndexOf(model.Source), i => new SourceChosen(i)),
            H.Choice("to", "To", accounts, IndexOf(model.Target), i => new TargetChosen(i)),
            model.AccountsError is { } accountsError ? H.Text(accountsError, Tone.Error) : null,
            H.TextBox("amount", "Amount", model.AmountText, text => new AmountTyped(text)),
            model.AmountError is { } amountError ? H.Text(amountError, Tone.Error) : null,
            H.Button("swap", "Swap", model.Source is not null || model.Target is not null ? new SwapClicked() : null),
            H.Button("transfer", model.Busy ? "Transfer…" : "Transfer", model.CanTransfer ? new TransferClicked() : null),
            model.PendingConfirmation is { } question
                ? H.Column(H.Text(question), H.Button("yes", "Yes", new ConfirmationAnswered(true)), H.Button("no", "No", new ConfirmationAnswered(false)))
                : null,
            model.Notice is { } notice ? H.Text(notice.Text, notice.IsError ? Tone.Error : Tone.Success) : null);
    }
}
