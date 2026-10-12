namespace Components.Accounts;

// internal: invisible outside the component. Nobody can do "new Account()" from the API.
internal class Account
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

    public Result Withdraw(decimal amount)
    {
        if (amount <= 0)
            return Result.Refused("The amount must be strictly positive.");
        if (amount > Balance)
            return Result.Refused("Insufficient balance.");
        Balance -= amount;
        return Result.Ok();
    }

    public Result Deposit(decimal amount)
    {
        if (amount <= 0)
            return Result.Refused("The amount must be strictly positive.");
        Balance += amount;
        return Result.Ok();
    }

    public AccountDto ToDto() => new(Id, Owner, Balance);
}
