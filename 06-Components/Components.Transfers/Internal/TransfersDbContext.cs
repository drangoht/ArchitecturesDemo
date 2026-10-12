using Microsoft.EntityFrameworkCore;

namespace Components.Transfers;

internal class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public decimal Amount { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}

internal class TransfersDbContext(DbContextOptions<TransfersDbContext> options) : DbContext(options)
{
    public DbSet<Transfer> Transfers => Set<Transfer>();
}
