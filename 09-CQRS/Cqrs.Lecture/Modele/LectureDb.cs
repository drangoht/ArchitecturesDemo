using Microsoft.EntityFrameworkCore;

namespace Cqrs.Lecture.Modele;

// Modèles de lecture : de simples sacs de données, taillés pour les écrans.
// On y duplique volontairement des informations (titulaire, solde après opération…) pour ne jamais avoir à calculer.

/// <summary>Écran « mon compte » : tout est déjà calculé.</summary>
public class ReleveCompte
{
    public Guid Id { get; set; }
    public string Titulaire { get; set; } = "";
    public decimal Solde { get; set; }
    public int NombreOperations { get; set; }
    public DateTime DerniereOperationLe { get; set; }
}

/// <summary>Écran « historique » : une ligne par opération, libellé déjà rédigé.</summary>
public class LigneOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompteId { get; set; }
    public string Libelle { get; set; } = "";
    public decimal Montant { get; set; }
    public decimal SoldeApres { get; set; }
    public DateTime Date { get; set; }
}

/// <summary>Écran « tableau de bord » : des agrégats maintenus au fil de l'eau (une seule ligne).</summary>
public class Statistiques
{
    public int Id { get; set; } = 1;
    public int NombreComptes { get; set; }
    public decimal EncoursTotal { get; set; }
    public int NombreVirements { get; set; }
    public decimal MontantTotalVire { get; set; }
}

/// <summary>Dernier événement projeté : permet de reprendre là où on s'était arrêté et d'ignorer les doublons.</summary>
public class PointDeReprise
{
    public int Id { get; set; } = 1;
    public long DerniereSequence { get; set; }
}

public class LectureDb(DbContextOptions<LectureDb> options) : DbContext(options)
{
    public DbSet<ReleveCompte> Releves => Set<ReleveCompte>();
    public DbSet<LigneOperation> Operations => Set<LigneOperation>();
    public DbSet<Statistiques> Statistiques => Set<Statistiques>();
    public DbSet<PointDeReprise> PointsDeReprise => Set<PointDeReprise>();
}
