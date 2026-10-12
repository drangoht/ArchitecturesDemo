namespace Functional.Core;

// The rules DO nothing: they return a decision that the shell will apply.
// Each decision is a small closed hierarchy (Accepted / Refused), usable with pattern matching.

public abstract record WithdrawalDecision
{
    public sealed record Accepted(Account UpdatedAccount, decimal Amount) : WithdrawalDecision;
    public sealed record Refused(string Reason) : WithdrawalDecision;
}

public abstract record TransferDecision
{
    public sealed record Accepted(Account Source, Account Target, decimal Amount) : TransferDecision;
    public sealed record Refused(string Reason) : TransferDecision;
}
