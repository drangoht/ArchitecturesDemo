using Cqrs.Evenements;

namespace Cqrs.Ecriture.Domaine;

/// <summary>
/// Agrégat : il protège ses invariants et note ce qui lui arrive sous forme d'événements.
/// Il n'expose que ce dont les commandes ont besoin. Pas de liste d'opérations, pas de statistiques.
/// </summary>
public class Compte
{
    private readonly List<IEvenementDomaine> _evenements = [];

    public Guid Id { get; private set; }
    public string Titulaire { get; private set; }
    public decimal Solde { get; private set; }

    public IReadOnlyList<IEvenementDomaine> Evenements => _evenements;

    private Compte(Guid id, string titulaire, decimal solde)
    {
        Id = id;
        Titulaire = titulaire;
        Solde = solde;
    }

    public static Compte Ouvrir(Guid id, string titulaire, decimal depotInitial, DateTime date)
    {
        var compte = new Compte(id, titulaire, depotInitial);
        compte._evenements.Add(new CompteOuvert(id, titulaire, depotInitial, date));
        return compte;
    }

    public void Retirer(decimal montant, DateTime date)
    {
        Debiter(montant);
        _evenements.Add(new ArgentRetire(Id, montant, Solde, date));
    }

    /// <summary>Utilisés par le virement, qui produit son propre événement (il concerne deux comptes).</summary>
    public void Debiter(decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être strictement positif.");
        if (montant > Solde)
            throw new RegleMetierException("Solde insuffisant.");
        Solde -= montant;
    }

    public void Crediter(decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être strictement positif.");
        Solde += montant;
    }

    public IReadOnlyList<IEvenementDomaine> ExtraireEvenements()
    {
        var evenements = _evenements.ToList();
        _evenements.Clear();
        return evenements;
    }
}

public class RegleMetierException(string message) : Exception(message);

public class CompteIntrouvableException(Guid compteId) : Exception($"Compte {compteId} introuvable.");
