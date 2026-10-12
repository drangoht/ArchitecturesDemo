using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Micro.Accounts;

/// <summary>Marker type of the service (used by the tests to start THIS service).</summary>
public sealed class Marker;

public class Account(Guid id, string owner, decimal balance)
{
    public Guid Id { get; private set; } = id;
    public string Owner { get; private set; } = owner;
    public decimal Balance { get; private set; } = balance;

    /// <returns>null if the movement is done, otherwise the reason for the refusal.</returns>
    public string? Debit(decimal amount)
    {
        if (amount <= 0) return "The amount must be strictly positive.";
        if (amount > Balance) return "Insufficient balance.";
        Balance -= amount;
        return null;
    }

    public string? Credit(decimal amount)
    {
        if (amount <= 0) return "The amount must be strictly positive.";
        Balance += amount;
        return null;
    }
}

/// <summary>Memory of the operations already processed, to make the calls between services idempotent.</summary>
public class ProcessedOperation
{
    public string OperationId { get; set; } = "";
    public bool Accepted { get; set; }
    public string? Error { get; set; }
    public decimal BalanceAfter { get; set; }
}

/// <summary>
/// Outbox: the message to publish is saved in the SAME transaction as the business change.
/// A relay then sends it to the broker. If the broker is down, nothing is lost.
/// </summary>
public class OutboxMessage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Topic { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }

    public static OutboxMessage Create(string topic, object content) =>
        new() { Topic = topic, Body = JsonSerializer.Serialize(content, Json) };
}

public class AccountsDb(DbContextOptions<AccountsDb> options) : DbContext(options)
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ProcessedOperation> ProcessedOperations => Set<ProcessedOperation>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ProcessedOperation>().HasKey(o => o.OperationId);

    public void SeedDemoData()
    {
        if (Accounts.Any())
            return;
        Accounts.AddRange(new Account(Alice, "Alice", 1000m), new Account(Bob, "Bob", 500m));
        SaveChanges();
    }
}
