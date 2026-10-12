namespace Components.Accounts;

// ─── Public surface of the component: this is all the rest of the application can see. ───

public interface IAccountsComponent
{
    Task<AccountDto> Open(string owner, decimal initialDeposit, Guid? id = null);
    Task<AccountDto?> Get(Guid accountId);
    Task<Result> Withdraw(Guid accountId, decimal amount);
    Task<Result> Deposit(Guid accountId, decimal amount);
}

public record AccountDto(Guid Id, string Owner, decimal Balance);

public enum ResultStatus { Ok, Refused, NotFound }

public record Result(ResultStatus Status, string? Error = null)
{
    public bool Success => Status == ResultStatus.Ok;

    public static Result Ok() => new(ResultStatus.Ok);
    public static Result Refused(string reason) => new(ResultStatus.Refused, reason);
    public static Result NotFound(Guid accountId) => new(ResultStatus.NotFound, $"Account {accountId} not found.");
}
