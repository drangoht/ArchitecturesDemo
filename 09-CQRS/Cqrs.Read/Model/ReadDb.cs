using Microsoft.EntityFrameworkCore;

namespace Cqrs.Read.Model;

// Read models: mere bags of data, cut out for the screens.
// Information is deliberately duplicated (owner, balance after the operation…) so as never to have to compute.

/// <summary>"My account" screen: everything is already computed.</summary>
public class AccountStatement
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public decimal Balance { get; set; }
    public int OperationCount { get; set; }
    public DateTime LastOperationAt { get; set; }
}

/// <summary>"History" screen: one line per operation, label already written.</summary>
public class OperationLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string Label { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime Date { get; set; }
}

/// <summary>"Dashboard" screen: aggregates maintained as events flow in (a single row).</summary>
public class Statistics
{
    public int Id { get; set; } = 1;
    public int AccountCount { get; set; }
    public decimal TotalOutstanding { get; set; }
    public int TransferCount { get; set; }
    public decimal TotalTransferredAmount { get; set; }
}

/// <summary>Last projected event: lets the projection resume where it stopped and ignore duplicates.</summary>
public class Checkpoint
{
    public int Id { get; set; } = 1;
    public long LastSequence { get; set; }
}

public class ReadDb(DbContextOptions<ReadDb> options) : DbContext(options)
{
    public DbSet<AccountStatement> Statements => Set<AccountStatement>();
    public DbSet<OperationLine> Operations => Set<OperationLine>();
    public DbSet<Statistics> Statistics => Set<Statistics>();
    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();
}
