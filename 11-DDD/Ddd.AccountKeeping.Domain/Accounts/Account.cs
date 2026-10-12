using Ddd.AccountKeeping.Domain.ValueObjects;

namespace Ddd.AccountKeeping.Domain.Accounts;

public enum AccountStatus { Active, Blocked }

/// <summary>
/// Aggregate root. Every change goes through it, and it guarantees its invariants:
/// - the balance is never negative;
/// - the total of a day's withdrawals does not exceed the limit;
/// - a blocked account can no longer be debited.
/// It is the 2nd invariant that sets the BOUNDARY of the aggregate: to check it, the movements must be part of it.
/// </summary>
public class Account : AggregateRoot
{
    private readonly List<Movement> _movements = [];

    public AccountId Id { get; private set; }
    public Iban Iban { get; private set; } = null!;
    public OwnerName Owner { get; private set; } = null!;
    public Currency Currency { get; private set; } = null!;
    public Money Balance { get; private set; } = null!;
    public Money DailyWithdrawalLimit { get; private set; } = null!;
    public AccountStatus Status { get; private set; }
    public string? BlockReason { get; private set; }

    public IReadOnlyList<Movement> Movements => _movements;

    private Account() { }  // for EF Core

    /// <summary>Creation reserved for the factory (internal): an account is always born complete and valid.</summary>
    internal static Account Open(AccountId id, Iban iban, OwnerName owner, Money initialDeposit, Money limit, DateTime date)
    {
        var account = new Account
        {
            Id = id, Iban = iban, Owner = owner, Currency = initialDeposit.Currency,
            Balance = initialDeposit, DailyWithdrawalLimit = limit, Status = AccountStatus.Active
        };
        account._movements.Add(new Movement(date, MovementType.Deposit, initialDeposit, "Initial deposit"));
        account.Raise(new AccountOpened(id, iban, owner, initialDeposit, date));
        return account;
    }

    public void Withdraw(Money amount, DateTime date)
    {
        CheckDebitPossible(amount);

        var alreadyWithdrawn = WithdrawalsOfTheDay(date);
        if (alreadyWithdrawn + amount > DailyWithdrawalLimit)
            throw new BusinessRuleException(
                $"Daily withdrawal limit of {DailyWithdrawalLimit} exceeded (already withdrawn today: {alreadyWithdrawn}).");

        Balance -= amount;
        _movements.Add(new Movement(date, MovementType.Withdrawal, amount, "Withdrawal"));
        Raise(new WithdrawalMade(Id, amount, date));
    }

    public void Block(string reason, DateTime date)
    {
        if (Status == AccountStatus.Blocked)
            return;
        Status = AccountStatus.Blocked;
        BlockReason = reason;
        Raise(new AccountBlocked(Id, reason, date));
    }

    // ─── Used by the TransferService domain service (same assembly) ───

    internal void Debit(Money amount, string label, DateTime date)
    {
        CheckDebitPossible(amount);
        Balance -= amount;
        _movements.Add(new Movement(date, MovementType.TransferSent, amount, label));
    }

    internal void Credit(Money amount, string label, DateTime date)
    {
        CheckAmount(amount);
        Balance += amount;
        _movements.Add(new Movement(date, MovementType.TransferReceived, amount, label));
    }

    internal void Report(IDomainEvent @event) => Raise(@event);

    // ─── Rules ───

    private Money WithdrawalsOfTheDay(DateTime date) =>
        _movements
            .Where(m => m.Type == MovementType.Withdrawal && m.Date.Date == date.Date)
            .Aggregate(Money.Zero(Currency), (total, m) => total + m.Amount);

    private void CheckDebitPossible(Money amount)
    {
        if (Status == AccountStatus.Blocked)
            throw new BusinessRuleException($"This account is blocked ({BlockReason}).");
        CheckAmount(amount);
        if (amount > Balance)
            throw new BusinessRuleException($"Insufficient balance (balance: {Balance}).");
    }

    private void CheckAmount(Money amount)
    {
        if (amount.Currency != Currency)
            throw new BusinessRuleException($"This account is kept in {Currency}, not in {amount.Currency}.");
        if (amount.IsZero)
            throw new BusinessRuleException("The amount must be strictly positive.");
    }
}

public enum MovementType { Deposit, Withdrawal, TransferSent, TransferReceived }

/// <summary>
/// Entity INTERNAL to the aggregate: it has an identity, but does not exist outside its account.
/// No repository loads it on its own; it is always reached through the root.
/// </summary>
public class Movement
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime Date { get; private set; }
    public MovementType Type { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string Label { get; private set; } = "";

    private Movement() { }  // for EF Core

    internal Movement(DateTime date, MovementType type, Money amount, string label) =>
        (Date, Type, Amount, Label) = (date, type, amount, label);
}
