namespace Clean.Entities;

public class Account
{
    public Guid Id { get; }
    public string Owner { get; }
    public decimal Balance { get; private set; }

    public Account(Guid id, string owner, decimal balance)
    {
        Id = id;
        Owner = owner;
        Balance = balance;
    }

    public void Withdraw(decimal amount)
    {
        CheckAmount(amount);
        if (amount > Balance)
            throw new BusinessRuleException("Insufficient balance.");
        Balance -= amount;
    }

    public void Deposit(decimal amount)
    {
        CheckAmount(amount);
        Balance += amount;
    }

    private static void CheckAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("The amount must be strictly positive.");
    }
}

public class BusinessRuleException(string message) : Exception(message);
