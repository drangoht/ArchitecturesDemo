using Modular.Shared;

namespace Modular.Accounts.Contracts;

// ─── Synchronous communication: an API the other modules can call ───

public interface IAccountsApi
{
    Task<AccountInfo?> Get(Guid accountId);
    Task<OperationResult> Debit(Guid accountId, decimal amount);
    Task<OperationResult> Credit(Guid accountId, decimal amount);
}

public record AccountInfo(Guid Id, string Owner, decimal Balance);

public enum OperationStatus { Ok, Refused, NotFound }

public record OperationResult(OperationStatus Status, string? Error = null)
{
    public bool Success => Status == OperationStatus.Ok;

    public static OperationResult Ok() => new(OperationStatus.Ok);
    public static OperationResult Refused(string reason) => new(OperationStatus.Refused, reason);
    public static OperationResult NotFound(Guid id) => new(OperationStatus.NotFound, $"Account {id} not found.");
}

// ─── Asynchronous communication: the events the module publishes ───

public record WithdrawalMade(Guid AccountId, decimal Amount, decimal NewBalance) : IIntegrationEvent;
