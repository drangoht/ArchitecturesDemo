namespace Vertical.Api.Domain;

/// <summary>
/// Entity shared by several slices.
/// At first, the "sufficient balance" rule was written in WithdrawMoney; when MakeTransfer
/// needed it too, it was moved up here. Only what is shared is extracted into the domain.
/// </summary>
public class Account
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public decimal Balance { get; private set; }

    public Account(Guid id, string owner, decimal balance)
    {
        Id = id;
        Owner = owner;
        Balance = balance;
    }

    /// <returns>null if the withdrawal is done, otherwise the reason for the refusal.</returns>
    public string? Withdraw(decimal amount)
    {
        if (amount > Balance)
            return "Insufficient balance.";
        Balance -= amount;
        return null;
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
