using System.Diagnostics;

namespace Functional.Core;

public static class Rules
{
    public const decimal DailyTransferLimit = 2_000m;

    public static WithdrawalDecision Withdraw(Account account, decimal amount) => amount switch
    {
        <= 0 => new WithdrawalDecision.Refused("The amount must be strictly positive."),
        _ when amount > account.Balance => new WithdrawalDecision.Refused("Insufficient balance."),
        _ => new WithdrawalDecision.Accepted(account with { Balance = account.Balance - amount }, amount)
    };

    /// <param name="alreadyTransferredToday">
    /// What depends on time or on the database is computed by the shell and passed as a parameter:
    /// the core never calls DateTime.Now nor the database.
    /// </param>
    public static TransferDecision Transfer(Account source, Account target, decimal amount, decimal alreadyTransferredToday)
    {
        if (source.Id == target.Id)
            return new TransferDecision.Refused("Cannot transfer to the same account.");

        if (alreadyTransferredToday + amount > DailyTransferLimit)
            return new TransferDecision.Refused(
                $"Daily limit of {DailyTransferLimit:0} € exceeded (already transferred today: {alreadyTransferredToday:0.00} €).");

        // The withdrawal rule is reused: pure functions compose naturally.
        return Withdraw(source, amount) switch
        {
            WithdrawalDecision.Accepted withdrawal => new TransferDecision.Accepted(
                withdrawal.UpdatedAccount,
                target with { Balance = target.Balance + amount },
                amount),
            WithdrawalDecision.Refused refusal => new TransferDecision.Refused(refusal.Reason),
            _ => throw new UnreachableException()
        };
    }
}
