using Simple.Data;

namespace Simple.Records;

/// <summary>Another Active Record, read-only: the operations are written by Account.Save().</summary>
public class Operation
{
    private Operation(OperationRow row)
    {
        Label = row.Label;
        Amount = row.Amount;
        DateUtc = row.DateUtc;
    }

    public string Label { get; }
    public decimal Amount { get; }
    public DateTime DateUtc { get; }

    public static IReadOnlyList<Operation> LatestFor(Transaction tx, Guid accountId, int count) =>
        [.. tx.Operations.Where(o => o.AccountId == accountId)
            .OrderByDescending(o => o.DateUtc)
            .Take(count)
            .Select(o => new Operation(o))];
}

public class RuleViolationException(string message) : Exception(message);
