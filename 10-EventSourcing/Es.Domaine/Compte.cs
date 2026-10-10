namespace Es.Domaine;

/// <summary>Photo de l'état d'un compte à une version donnée, pour éviter de rejouer tout l'historique.</summary>
public record InstantaneCompte(Guid Id, string Titulaire, decimal Solde, bool Cloture, int Version);

/// <summary>
/// Agrégat « event-sourcé ». Deux sortes de méthodes, à ne jamais mélanger :
/// - les DÉCISIONS (Retirer, Cloturer…) vérifient les règles puis enregistrent un événement ;
/// - Appliquer fait évoluer l'état à partir d'un événement, sans aucune règle (un fait passé ne se refuse pas).
/// Charger un compte = rejouer Appliquer sur tout son historique.
/// </summary>
public class Compte
{
    private readonly List<IEvenement> _nouveaux = [];

    public Guid Id { get; private set; }
    public string Titulaire { get; private set; } = "";
    public decimal Solde { get; private set; }
    public bool Cloture { get; private set; }

    /// <summary>Nombre d'événements appliqués (historique + nouveaux).</summary>
    public int Version { get; private set; }

    /// <summary>Version telle que chargée depuis le magasin : sert à détecter les modifications concurrentes.</summary>
    public int VersionChargee { get; private set; }

    public IReadOnlyList<IEvenement> NouveauxEvenements => _nouveaux;

    private Compte() { }

    // ─── Reconstitution ───

    public static Compte Reconstituer(IEnumerable<IEvenement> historique) =>
        DepuisInstantane(null, historique);

    public static Compte DepuisInstantane(InstantaneCompte? instantane, IEnumerable<IEvenement> suite)
    {
        var compte = new Compte();
        if (instantane is not null)
        {
            (compte.Id, compte.Titulaire, compte.Solde, compte.Cloture, compte.Version) =
                (instantane.Id, instantane.Titulaire, instantane.Solde, instantane.Cloture, instantane.Version);
        }
        foreach (var evenement in suite)
            compte.Appliquer(evenement);
        compte.VersionChargee = compte.Version;
        return compte;
    }

    public InstantaneCompte Instantane() => new(Id, Titulaire, Solde, Cloture, Version);

    // ─── Décisions ───

    public static Compte Ouvrir(Guid id, string titulaire, decimal depotInitial, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(titulaire))
            throw new RegleMetierException("Le titulaire est obligatoire.");
        if (depotInitial < 10)
            throw new RegleMetierException("Le dépôt initial doit être d'au moins 10 €.");

        var compte = new Compte();
        compte.Enregistrer(new CompteOuvert(id, titulaire.Trim(), depotInitial, date));
        return compte;
    }

    public void Deposer(decimal montant, DateTime date)
    {
        VerifierOuvert();
        VerifierMontant(montant);
        Enregistrer(new ArgentDepose(Id, montant, date));
    }

    public void Retirer(decimal montant, string motif, DateTime date)
    {
        VerifierOuvert();
        VerifierMontant(montant);
        if (montant > Solde)
            throw new RegleMetierException("Solde insuffisant.");
        Enregistrer(new ArgentRetire(Id, montant, motif, date));
    }

    public void EmettreVirement(Guid virementId, Guid vers, decimal montant, DateTime date)
    {
        VerifierOuvert();
        VerifierMontant(montant);
        if (montant > Solde)
            throw new RegleMetierException("Solde insuffisant.");
        Enregistrer(new VirementEmis(Id, virementId, vers, montant, date));
    }

    public void RecevoirVirement(Guid virementId, Guid de, decimal montant, DateTime date)
    {
        VerifierOuvert();
        Enregistrer(new VirementRecu(Id, virementId, de, montant, date));
    }

    public void Cloturer(DateTime date)
    {
        VerifierOuvert();
        if (Solde != 0)
            throw new RegleMetierException($"Le solde doit être nul pour clôturer le compte (solde actuel : {Solde:0.00} €).");
        Enregistrer(new CompteCloture(Id, date));
    }

    /// <summary>Appelé une fois les nouveaux événements écrits dans le magasin.</summary>
    public void MarquerEnregistre()
    {
        _nouveaux.Clear();
        VersionChargee = Version;
    }

    // ─── Évolution de l'état ───

    private void Enregistrer(IEvenement evenement)
    {
        Appliquer(evenement);
        _nouveaux.Add(evenement);
    }

    private void Appliquer(IEvenement evenement)
    {
        switch (evenement)
        {
            case CompteOuvert e:
                (Id, Titulaire, Solde) = (e.CompteId, e.Titulaire, e.DepotInitial);
                break;
            case ArgentDepose e:
                Solde += e.Montant;
                break;
            case ArgentRetire e:
                Solde -= e.Montant;
                break;
            case VirementEmis e:
                Solde -= e.Montant;
                break;
            case VirementRecu e:
                Solde += e.Montant;
                break;
            case CompteCloture:
                Cloture = true;
                break;
        }
        Version++;
    }

    private void VerifierOuvert()
    {
        if (Cloture)
            throw new RegleMetierException("Ce compte est clôturé.");
    }

    private static void VerifierMontant(decimal montant)
    {
        if (montant <= 0)
            throw new RegleMetierException("Le montant doit être strictement positif.");
    }
}

public class RegleMetierException(string message) : Exception(message);

public class CompteIntrouvableException(Guid compteId) : Exception($"Compte {compteId} introuvable.");
