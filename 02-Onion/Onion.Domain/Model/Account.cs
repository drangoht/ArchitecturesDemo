namespace Onion.Domain.Model;

// Layer 1 — Domain Model: the entities and their invariants.
public class Account
{
    public Guid Id { get; private set; }
    public string Owner { get; private set; }
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
