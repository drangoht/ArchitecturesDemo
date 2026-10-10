using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Micro.Virements;

/// <summary>Type repère du service (utilisé par les tests pour démarrer CE service).</summary>
public sealed class Marqueur;

/// <summary>
/// États d'un virement. Le virement est enregistré AVANT d'appeler Comptes :
/// si un appel échoue en route, on sait où il en était (principe de la saga).
/// </summary>
public enum StatutVirement { EnCours, Effectue, Refuse, Annule, AReprendre }

public class Virement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceId { get; set; }
    public Guid CibleId { get; set; }
    public decimal Montant { get; set; }
    public StatutVirement Statut { get; set; } = StatutVirement.EnCours;
    public string? Erreur { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}

public class MessageOutbox
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sujet { get; set; } = "";
    public string Corps { get; set; } = "";
    public DateTime CreeLe { get; set; } = DateTime.UtcNow;
    public DateTime? EnvoyeLe { get; set; }

    public static MessageOutbox Creer(string sujet, object contenu) =>
        new() { Sujet = sujet, Corps = JsonSerializer.Serialize(contenu, Json) };
}

public class VirementsDb(DbContextOptions<VirementsDb> options) : DbContext(options)
{
    public DbSet<Virement> Virements => Set<Virement>();
    public DbSet<MessageOutbox> Outbox => Set<MessageOutbox>();
}
