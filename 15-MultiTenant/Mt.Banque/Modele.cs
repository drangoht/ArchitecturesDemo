namespace Mt.Banque;

/// <summary>
/// Marque une donnée qui appartient à un locataire. <see cref="BanqueDbContext"/> s'en sert pour filtrer
/// toutes les requêtes et estampiller toutes les insertions : il suffit d'implémenter l'interface, impossible d'oublier.
/// </summary>
public interface IAppartientAUnLocataire
{
    string LocataireId { get; }
}

public sealed class Compte : IAppartientAUnLocataire
{
    private Compte() { }

    public Compte(Guid id, string titulaire, decimal solde)
    {
        Id = id;
        Titulaire = titulaire;
        Solde = solde;
    }

    /// <summary>Jamais affecté par le métier : renseigné par le DbContext à l'enregistrement.</summary>
    public string LocataireId { get; private set; } = "";
    public Guid Id { get; private set; }
    public string Titulaire { get; private set; } = "";
    public decimal Solde { get; private set; }

    public void Debiter(decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être positif.");
        if (montant > Solde)
            throw new RegleMetierException($"Solde insuffisant ({Solde:0.00} €).");
        Solde -= montant;
    }

    public void Crediter(decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être positif.");
        Solde += montant;
    }
}

public sealed class Operation : IAppartientAUnLocataire
{
    private Operation() { }

    public Operation(Guid compteId, DateTimeOffset date, string libelle, decimal montant, string? cle = null)
    {
        Id = Guid.NewGuid();
        CompteId = compteId;
        Date = date;
        Libelle = libelle;
        Montant = montant;
        Cle = cle;
    }

    public string LocataireId { get; private set; } = "";
    public Guid Id { get; private set; }
    public Guid CompteId { get; private set; }
    public DateTimeOffset Date { get; private set; }
    public string Libelle { get; private set; } = "";
    public decimal Montant { get; private set; }

    /// <summary>Clé d'idempotence (ex. « frais-2026-10 ») : un prélèvement rejoué ne débite pas deux fois.</summary>
    public string? Cle { get; private set; }
}

public sealed class RegleMetierException(string message) : Exception(message);

public sealed class CompteIntrouvableException(Guid id) : Exception($"Compte {id} introuvable.");

/// <summary>Une écriture visait une donnée d'un autre locataire : c'est un bogue, jamais un cas métier.</summary>
public sealed class FuiteEntreLocatairesException(string message) : InvalidOperationException(message);

public sealed record CompteDto(Guid Id, string Titulaire, decimal Solde);

public sealed record OperationDto(DateTimeOffset Date, string Libelle, decimal Montant);

public sealed record VirementDto(Guid Source, Guid Cible, decimal Montant, decimal Frais, decimal SoldeSource);
