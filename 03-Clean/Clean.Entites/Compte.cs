namespace Clean.Entites;

public class Compte
{
    public Guid Id { get; }
    public string Titulaire { get; }
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

public class RegleMetierException(string message) : Exception(message);
