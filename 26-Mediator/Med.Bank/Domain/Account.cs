namespace Med.Bank.Domain;

public class Account(Guid id, string owner, decimal balance)
{
    public Guid Id { get; private set; } = id;
    public string Owner { get; private set; } = owner;
    public decimal Balance { get; private set; } = balance;

    public void Withdraw(decimal amount)
    {
        if (amount > Balance)
            throw new BusinessRuleException("Insufficient balance.");
        Balance -= amount;
    }

    public void Deposit(decimal amount) => Balance += amount;
}

public class Operation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Message { get; set; } = "";
}

public class ComplianceAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reason { get; set; } = "";
}

public class BusinessRuleException(string message) : Exception(message);

public class NotFoundException(string message) : Exception(message);
