using Mvu.Runtime;
using Ui.Bank;

namespace Mvu.Bank;

/// <summary>
/// The screen's logic: ONE pure function. A message and the current model come in; the new model and the effects to
/// run go out. No await, no event, no shared state: every behaviour can be read here, and tested with a value in
/// and a value out. Compare with TransferViewModel (demo 24), where the same behaviours are spread over setters,
/// Validate(), events and an async command.
/// </summary>
public static class BankScreen
{
    private static readonly IReadOnlyList<Effect> None = [];

    public static (Model, IReadOnlyList<Effect>) Init() => (Model.Empty, [new LoadAccounts()]);

    public static (Model, IReadOnlyList<Effect>) Update(Msg message, Model model) => message switch
    {
        AccountsLoaded loaded => Select(model with { Accounts = [.. loaded.Accounts] },
            model.Selected ?? loaded.Accounts.FirstOrDefault()?.Id),

        AccountSelected selected when selected.Index < model.Accounts.Count =>
            Select(model, model.Accounts[selected.Index].Id),

        // A response to an older request (A clicked, then B, and A's answer arrives last): ignored.
        OperationsLoaded loaded when loaded.Request != model.OperationsRequest => (model, None),
        OperationsLoaded loaded => (model with { Operations = [.. loaded.Operations], LoadingOperations = false }, None),
        OperationsFailed failed when failed.Request == model.OperationsRequest =>
            (model with { LoadingOperations = false, Notice = new Notice(failed.Error, IsError: true) }, None),

        SourceChosen chosen when chosen.Index < model.Accounts.Count =>
            (model with { Source = model.Accounts[chosen.Index].Id }, None),
        TargetChosen chosen when chosen.Index < model.Accounts.Count =>
            (model with { Target = model.Accounts[chosen.Index].Id }, None),
        AmountTyped typed => (model with { AmountText = typed.Text }, None),
        SwapClicked => (model with { Source = model.Target, Target = model.Source }, None),

        // The button is greyed out when this is false; a click that arrives anyway (double click) changes nothing.
        TransferClicked when !model.CanTransfer => (model, None),
        TransferClicked when model.Amount >= TransferRules.ConfirmationThreshold =>
            (model with { PendingConfirmation = Question(model) }, None),
        TransferClicked => Send(model),

        ConfirmationAnswered { Yes: true } when model.PendingConfirmation is not null =>
            Send(model with { PendingConfirmation = null }),
        ConfirmationAnswered when model.PendingConfirmation is not null =>
            (model with { PendingConfirmation = null, Notice = new Notice("Transfer cancelled.", IsError: false) }, None),

        TransferDone done => (model with
        {
            Busy = false,
            AmountText = "",
            Notice = new Notice($"Transfer of {Format.Euros(done.Transfer.Amount)} done. New balance: {Format.Euros(done.Transfer.SourceBalance)}.", IsError: false)
        }, [new LoadAccounts()]),   // the balances changed: reload them (and the history)
        TransferFailed failed => (model with { Busy = false, Notice = new Notice(failed.Error, IsError: true) }, None),

        _ => (model, None),
    };

    private static (Model, IReadOnlyList<Effect>) Select(Model model, Guid? account)
    {
        if (account is not { } id)
            return (model, None);
        var request = model.OperationsRequest + 1;
        return (model with { Selected = id, Operations = [], OperationsRequest = request, LoadingOperations = true },
            [new LoadOperations(id, request)]);
    }

    private static (Model, IReadOnlyList<Effect>) Send(Model model) =>
        (model with { Busy = true, Notice = new Notice("Transfer in progress…", IsError: false) },
            [new SendTransfer(model.Source!.Value, model.Target!.Value, model.Amount!.Value)]);

    private static string Question(Model model)
    {
        var source = model.Accounts.Find(a => a.Id == model.Source)!;
        var target = model.Accounts.Find(a => a.Id == model.Target)!;
        return $"Transfer {Format.Euros(model.Amount!.Value)} from {AccountText.Display(source)} to {AccountText.Display(target)}?";
    }

    public static Program<Model, Msg, Effect> Program { get; } = new(Init, Update, BankView.View);
}
