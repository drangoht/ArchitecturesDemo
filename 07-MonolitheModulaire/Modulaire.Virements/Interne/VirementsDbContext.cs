using Microsoft.EntityFrameworkCore;

namespace Modulaire.Virements;

internal class Virement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid CibleId { get; set; }
    public decimal Montant { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}

internal class VirementsDbContext(DbContextOptions<VirementsDbContext> options) : DbContext(options)
{
    public DbSet<Virement> Virements => Set<Virement>();
}
