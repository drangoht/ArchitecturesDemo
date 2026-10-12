namespace Actors.Bank;

// The messages are immutable records: once dropped off, neither the sender nor the recipient can modify them.
// It is the only way to exchange data between actors, no state is shared.

// ── Messages of an account ──
public sealed record OpenAccount(string Owner, decimal InitialDeposit);
public sealed record GetAccount;
public sealed record ListOperations;
public sealed record Deposit(decimal Amount);
public sealed record Withdraw(decimal Amount);

/// <summary>Debit requested by a transfer: the <paramref name="Key"/> makes the repetition harmless (idempotence).</summary>
public sealed record Debit(string Key, decimal Amount, string Label);

/// <summary>Credit requested by a transfer, idempotent as well.</summary>
public sealed record Credit(string Key, decimal Amount, string Label);

// ── Messages of a transfer ──
public sealed record ExecuteTransfer(Guid Source, Guid Target, decimal Amount);
public sealed record GetTransfer;

// ── Responses ──
public sealed record AccountDto(Guid Id, string Owner, decimal Balance);
public sealed record OperationDto(DateTimeOffset Date, string Label, decimal Amount);
public sealed record TransferDto(string Id, Guid Source, Guid Target, decimal Amount, TransferStatus Status, string? Reason);

/// <summary>A business refusal: a normal response, not a failure (the actor quietly carries on).</summary>
public sealed record Refusal(string Reason, bool NotFound = false);

public enum TransferStatus { InProgress, DebitDone, Completed, Refused, Cancelled }
