namespace Vertical.Api.Domain;

/// <summary>
/// Entité partagée par plusieurs tranches.
/// Au départ, la règle « solde suffisant » était écrite dans RetirerArgent ; quand EffectuerVirement
/// en a eu besoin aussi, elle a été remontée ici. On n'extrait vers le domaine que ce qui est partagé.
/// </summary>
public class Compte
{
    public Guid Id { get; set; }
    public string Titulaire { get; set; } = "";
    public decimal Solde { get; private set; }

    public Compte(Guid id, string titulaire, decimal solde)
    {
        Id = id;
        Titulaire = titulaire;
        Solde = solde;
    }

    /// <returns>null si le retrait est fait, sinon la raison du refus.</returns>
    public string? Retirer(decimal montant)
    {
        if (montant > Solde)
            return "Solde insuffisant.";
        Solde -= montant;
        return null;
    }

    public void Deposer(decimal montant) => Solde += montant;
}

public class Operation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompteId { get; set; }
    public string Libelle { get; set; } = "";
    public decimal Montant { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}
