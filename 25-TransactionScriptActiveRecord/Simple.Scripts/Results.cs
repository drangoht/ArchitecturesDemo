namespace Simple.Scripts;

// What the scripts return: data shaped for the caller, not objects with behaviour.
public record AccountView(Guid Id, string Owner, decimal Balance, IReadOnlyList<OperationView> LatestOperations);

public record OperationView(string Label, decimal Amount, DateTime DateUtc);

public record WithdrawalResult(Guid AccountId, decimal NewBalance);

public record TransferResult(decimal SourceBalance, decimal TargetBalance);

public class ScriptRefusedException(string message) : Exception(message);

public class AccountNotFoundException(Guid accountId) : Exception($"Account {accountId} not found.");
