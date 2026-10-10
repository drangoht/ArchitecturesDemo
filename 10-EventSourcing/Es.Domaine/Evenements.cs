namespace Es.Domaine;

/// <summary>
/// Un fait passé, immuable. En Event Sourcing, la suite de ces faits EST la donnée :
/// il n'existe aucune table « Comptes » avec un solde. Le solde se déduit des événements.
/// </summary>
public interface IEvenement
{
    Guid CompteId { get; }
    DateTime Date { get; }
}

public record CompteOuvert(Guid CompteId, string Titulaire, decimal DepotInitial, DateTime Date) : IEvenement;

public record ArgentDepose(Guid CompteId, decimal Montant, DateTime Date) : IEvenement;

/// <summary>
/// Version 2 de l'événement : le motif a été ajouté après coup. Les anciens événements (v1, sans motif)
/// restent dans le magasin tels quels ; ils sont convertis à la lecture (« upcasting », voir Es.Stockage).
/// </summary>
public record ArgentRetire(Guid CompteId, decimal Montant, string Motif, DateTime Date) : IEvenement;

public record VirementEmis(Guid CompteId, Guid VirementId, Guid Vers, decimal Montant, DateTime Date) : IEvenement;

public record VirementRecu(Guid CompteId, Guid VirementId, Guid De, decimal Montant, DateTime Date) : IEvenement;

public record CompteCloture(Guid CompteId, DateTime Date) : IEvenement;
