using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Micro.Transfers;

/// <summary>Marker type of the service (used by the tests to start THIS service).</summary>
public sealed class Marker;

/// <summary>
/// States of a transfer. The transfer is saved BEFORE calling Accounts:
/// if a call fails along the way, we know where it stood (the saga principle).
/// </summary>
public enum TransferStatus { InProgress, Done, Refused, Cancelled, ToResume }

public class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public decimal Amount { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.InProgress;
    public string? Error { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}

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

public class TransfersDb(DbContextOptions<TransfersDb> options) : DbContext(options)
{
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
}
