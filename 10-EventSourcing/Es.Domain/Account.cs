namespace Es.Domain;

/// <summary>Snapshot of an account's state at a given version, to avoid replaying the whole history.</summary>
public record AccountSnapshot(Guid Id, string Owner, decimal Balance, bool Closed, int Version);

/// <summary>
/// "Event-sourced" aggregate. Two kinds of methods, never to be mixed:
/// - the DECISIONS (Withdraw, Close…) check the rules then record an event;
/// - Apply evolves the state from an event, without any rule (a past fact cannot be refused).
/// Loading an account = replaying Apply over its whole history.
/// </summary>
public class Account
{
    private readonly List<IEvent> _new = [];

    public Guid Id { get; private set; }
    public string Owner { get; private set; } = "";
    public decimal Balance { get; private set; }
    public bool Closed { get; private set; }

    /// <summary>Number of events applied (history + new ones).</summary>
    public int Version { get; private set; }

    /// <summary>Version as loaded from the store: used to detect concurrent changes.</summary>
    public int LoadedVersion { get; private set; }

    public IReadOnlyList<IEvent> NewEvents => _new;

    private Account() { }

    // ─── Rehydration ───

    public static Account Rehydrate(IEnumerable<IEvent> history) =>
        FromSnapshot(null, history);

    public static Account FromSnapshot(AccountSnapshot? snapshot, IEnumerable<IEvent> rest)
    {
        var account = new Account();
        if (snapshot is not null)
        {
            (account.Id, account.Owner, account.Balance, account.Closed, account.Version) =
                (snapshot.Id, snapshot.Owner, snapshot.Balance, snapshot.Closed, snapshot.Version);
        }
        foreach (var @event in rest)
            account.Apply(@event);
        account.LoadedVersion = account.Version;
        return account;
    }

    public AccountSnapshot Snapshot() => new(Id, Owner, Balance, Closed, Version);

    // ─── Decisions ───

    public static Account Open(Guid id, string owner, decimal initialDeposit, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(owner))
            throw new BusinessRuleException("The owner is required.");
        if (initialDeposit < 10)
            throw new BusinessRuleException("The initial deposit must be at least 10 €.");

        var account = new Account();
        account.Record(new AccountOpened(id, owner.Trim(), initialDeposit, date));
        return account;
    }

    public void Deposit(decimal amount, DateTime date)
    {
        CheckOpen();
        CheckAmount(amount);
        Record(new MoneyDeposited(Id, amount, date));
    }

    public void Withdraw(decimal amount, string reason, DateTime date)
    {
        CheckOpen();
        CheckAmount(amount);
        if (amount > Balance)
            throw new BusinessRuleException("Insufficient balance.");
        Record(new MoneyWithdrawn(Id, amount, reason, date));
    }

    public void SendTransfer(Guid transferId, Guid to, decimal amount, DateTime date)
    {
        CheckOpen();
        CheckAmount(amount);
        if (amount > Balance)
            throw new BusinessRuleException("Insufficient balance.");
        Record(new TransferSent(Id, transferId, to, amount, date));
    }

    public void ReceiveTransfer(Guid transferId, Guid from, decimal amount, DateTime date)
    {
        CheckOpen();
        Record(new TransferReceived(Id, transferId, from, amount, date));
    }

    public void Close(DateTime date)
    {
        CheckOpen();
        if (Balance != 0)
            throw new BusinessRuleException($"The balance must be zero to close the account (current balance: {Balance:0.00} €).");
        Record(new AccountClosed(Id, date));
    }

    /// <summary>Called once the new events are written to the store.</summary>
    public void MarkSaved()
    {
        _new.Clear();
        LoadedVersion = Version;
    }

    // ─── State evolution ───

    private void Record(IEvent @event)
    {
        Apply(@event);
        _new.Add(@event);
    }

    private void Apply(IEvent @event)
    {
        switch (@event)
        {
            case AccountOpened e:
                (Id, Owner, Balance) = (e.AccountId, e.Owner, e.InitialDeposit);
                break;
            case MoneyDeposited e:
                Balance += e.Amount;
                break;
            case MoneyWithdrawn e:
                Balance -= e.Amount;
                break;
            case TransferSent e:
                Balance -= e.Amount;
                break;
            case TransferReceived e:
                Balance += e.Amount;
                break;
            case AccountClosed:
                Closed = true;
                break;
        }
        Version++;
    }

    private void CheckOpen()
    {
        if (Closed)
            throw new BusinessRuleException("This account is closed.");
    }

    private static void CheckAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be strictly positive.");
    }
}

public class BusinessRuleException(string message) : Exception(message);

public class AccountNotFoundException(Guid accountId) : Exception($"Account {accountId} not found.");
