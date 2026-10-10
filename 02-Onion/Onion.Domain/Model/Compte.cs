namespace Onion.Domain.Model;

// Couche 1 — Domain Model : les entités et leurs invariants.
public class Compte
{
    public Guid Id { get; private set; }
    public string Titulaire { get; private set; }
    public decimal Solde { get; private set; }

    public Compte(Guid id, string titulaire, decimal solde)
    {
        Id = id;
        Titulaire = titulaire;
        Solde = solde;
    }

    public void Retirer(decimal montant)
    {
        VerifierMontant(montant);
        if (montant > Solde)
            throw new RegleMetierException("Solde insuffisant.");
        Solde -= montant;
    }

    public void Deposer(decimal montant)
    {
        VerifierMontant(montant);
        Solde += montant;
    }

    private static void VerifierMontant(decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être strictement positif.");
    }
}
