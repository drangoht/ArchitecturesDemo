namespace Composants.Comptes;

// internal : invisible hors du composant. Personne ne peut faire « new Compte() » depuis l'API.
internal class Compte
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

    public Resultat Retirer(decimal montant)
    {
        if (montant <= 0)
            return Resultat.Refuse("Le montant doit être strictement positif.");
        if (montant > Solde)
            return Resultat.Refuse("Solde insuffisant.");
        Solde -= montant;
        return Resultat.Ok();
    }

    public Resultat Deposer(decimal montant)
    {
        if (montant <= 0)
            return Resultat.Refuse("Le montant doit être strictement positif.");
        Solde += montant;
        return Resultat.Ok();
    }

    public CompteDto VersDto() => new(Id, Titulaire, Solde);
}
