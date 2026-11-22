using Ui.Bank;

namespace Mvu.Bank;

/// <summary>
/// EVERYTHING THAT CAN HAPPEN to the screen, as data: what the user did, and what the bank answered.
/// The list of messages is the list of the screen's behaviours.
/// </summary>
public abstract record Msg;

// What the user does
public sealed record AccountSelected(int Index) : Msg;
public sealed record SourceChosen(int Index) : Msg;
public sealed record TargetChosen(int Index) : Msg;
public sealed record AmountTyped(string Text) : Msg;
public sealed record SwapClicked : Msg;
public sealed record TransferClicked : Msg;
public sealed record ConfirmationAnswered(bool Yes) : Msg;

// What the bank answers (the results of the effects)
public sealed record AccountsLoaded(IReadOnlyList<Account> Accounts) : Msg;
public sealed record OperationsLoaded(int Request, IReadOnlyList<Operation> Operations) : Msg;
public sealed record OperationsFailed(int Request, string Error) : Msg;
public sealed record TransferDone(Transfer Transfer) : Msg;
public sealed record TransferFailed(string Error) : Msg;

/// <summary>
/// What Update ASKS for, as data: it doesn't call the bank, it says "load the accounts". The runtime has the effects
/// performed and turns their results into messages. Update stays a pure function, tested without any bank.
/// </summary>
public abstract record Effect;
public sealed record LoadAccounts : Effect;
public sealed record LoadOperations(Guid Account, int Request) : Effect;
public sealed record SendTransfer(Guid Source, Guid Target, decimal Amount) : Effect;
