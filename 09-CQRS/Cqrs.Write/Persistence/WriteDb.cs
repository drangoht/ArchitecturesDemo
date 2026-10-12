using Cqrs.Write.Domain;
using Cqrs.Events;
using Microsoft.EntityFrameworkCore;

namespace Cqrs.Write.Persistence;

public class StoredEvent
{
    public long Sequence { get; set; }
    public string Type { get; set; } = "";
    public string Data { get; set; } = "";
    public DateTime Date { get; set; }
}

/// <summary>
/// Database of the write side: the state of the aggregates + the event log, saved in the same transaction.
/// </summary>
public class WriteDb(DbContextOptions<WriteDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<StoredEvent> Events => Set<StoredEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(account =>
        {
            account.HasKey(c => c.Id);
            account.Ignore(c => c.Events);
        });
        modelBuilder.Entity<StoredEvent>(@event =>
        {
            @event.HasKey(e => e.Sequence);
            @event.Property(e => e.Sequence).ValueGeneratedOnAdd();
        });
    }

    /// <summary>Saves the changes and appends to the log the events produced by the aggregates.</summary>
    public async Task Save(params IDomainEvent[] extraEvents)
    {
        var events = ChangeTracker.Entries<Account>()
            .SelectMany(entry => entry.Entity.ExtractEvents())
            .Concat(extraEvents)
            .ToList();

        foreach (var @event in events)
            Events.Add(new StoredEvent
            {
                Type = @event.GetType().Name,
                Data = EventCatalog.Serialize(@event),
                Date = @event.Date
            });

        await SaveChangesAsync();
    }
}

internal class EventStream(WriteDb db) : IEventStream
{
    public async Task<IReadOnlyList<RecordedEvent>> ReadAfter(long sequence, int maximum) =>
        (await db.Events.AsNoTracking()
            .Where(e => e.Sequence > sequence)
            .OrderBy(e => e.Sequence)
            .Take(maximum)
            .ToListAsync())
        .Select(e => new RecordedEvent(e.Sequence, EventCatalog.Deserialize(e.Type, e.Data)))
        .ToList();

    public async Task<long> LastSequence() =>
        await db.Events.AnyAsync() ? await db.Events.MaxAsync(e => e.Sequence) : 0;
}
