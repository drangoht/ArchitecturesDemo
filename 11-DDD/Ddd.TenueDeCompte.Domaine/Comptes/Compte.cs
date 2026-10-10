using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;

namespace Ddd.TenueDeCompte.Domaine.Comptes;

public enum StatutCompte { Actif, Bloque }

/// <summary>
/// Racine d'agrégat. Tout changement passe par elle, et elle garantit ses invariants :
/// - le solde n'est jamais négatif ;
/// - le total des retraits d'une journée ne dépasse pas le plafond ;
/// - un compte bloqué ne peut plus être débité.
/// C'est le 2e invariant qui fixe la FRONTIÈRE de l'agrégat : pour le vérifier, les mouvements doivent en faire partie.
/// </summary>
public class Compte : RacineAgregat
{
    private readonly List<Mouvement> _mouvements = [];

    public CompteId Id { get; private set; }
    public Iban Iban { get; private set; } = null!;
    public NomTitulaire Titulaire { get; private set; } = null!;
    public Devise Devise { get; private set; } = null!;
    public Argent Solde { get; private set; } = null!;
    public Argent PlafondRetraitJournalier { get; private set; } = null!;
    public StatutCompte Statut { get; private set; }
    public string? MotifBlocage { get; private set; }

    public IReadOnlyList<Mouvement> Mouvements => _mouvements;

    private Compte() { }  // pour EF Core

    /// <summary>Création réservée à la fabrique (internal) : un compte naît toujours complet et valide.</summary>
    internal static Compte Ouvrir(CompteId id, Iban iban, NomTitulaire titulaire, Argent depotInitial, Argent plafond, DateTime date)
    {
        var compte = new Compte
        {
            Id = id, Iban = iban, Titulaire = titulaire, Devise = depotInitial.Devise,
            Solde = depotInitial, PlafondRetraitJournalier = plafond, Statut = StatutCompte.Actif
        };
        compte._mouvements.Add(new Mouvement(date, TypeMouvement.Depot, depotInitial, "Dépôt initial"));
        compte.Lever(new CompteOuvert(id, iban, titulaire, depotInitial, date));
        return compte;
    }

    public void Retirer(Argent montant, DateTime date)
    {
        VerifierDebitPossible(montant);

        var dejaRetire = RetraitsDuJour(date);
        if (dejaRetire + montant > PlafondRetraitJournalier)
            throw new RegleMetierException(
                $"Plafond de retrait journalier de {PlafondRetraitJournalier} dépassé (déjà retiré aujourd'hui : {dejaRetire}).");

        Solde -= montant;
        _mouvements.Add(new Mouvement(date, TypeMouvement.Retrait, montant, "Retrait"));
        Lever(new RetraitEffectue(Id, montant, date));
    }

    public void Bloquer(string motif, DateTime date)
    {
        if (Statut == StatutCompte.Bloque)
            return;
        Statut = StatutCompte.Bloque;
        MotifBlocage = motif;
        Lever(new CompteBloque(Id, motif, date));
    }

    // ─── Utilisés par le service de domaine ServiceDeVirement (même assembly) ───

    internal void Debiter(Argent montant, string libelle, DateTime date)
    {
        VerifierDebitPossible(montant);
        Solde -= montant;
        _mouvements.Add(new Mouvement(date, TypeMouvement.VirementEmis, montant, libelle));
    }

    internal void Crediter(Argent montant, string libelle, DateTime date)
    {
        VerifierMontant(montant);
        Solde += montant;
        _mouvements.Add(new Mouvement(date, TypeMouvement.VirementRecu, montant, libelle));
    }

    internal void Signaler(IEvenementDomaine evenement) => Lever(evenement);

    // ─── Règles ───

    private Argent RetraitsDuJour(DateTime date) =>
        _mouvements
            .Where(m => m.Type == TypeMouvement.Retrait && m.Date.Date == date.Date)
            .Aggregate(Argent.Zero(Devise), (total, m) => total + m.Montant);

    private void VerifierDebitPossible(Argent montant)
    {
        if (Statut == StatutCompte.Bloque)
            throw new RegleMetierException($"Ce compte est bloqué ({MotifBlocage}).");
        VerifierMontant(montant);
        if (montant > Solde)
            throw new RegleMetierException($"Solde insuffisant (solde : {Solde}).");
    }

    private void VerifierMontant(Argent montant)
    {
        if (montant.Devise != Devise)
            throw new RegleMetierException($"Ce compte est tenu en {Devise}, pas en {montant.Devise}.");
        if (montant.EstNul)
            throw new RegleMetierException("Le montant doit être strictement positif.");
    }
}

public enum TypeMouvement { Depot, Retrait, VirementEmis, VirementRecu }

/// <summary>
/// Entité INTERNE à l'agrégat : elle a une identité, mais n'existe pas hors de son compte.
/// Aucun dépôt (repository) ne la charge seule ; on y accède toujours par la racine.
/// </summary>
public class Mouvement
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime Date { get; private set; }
    public TypeMouvement Type { get; private set; }
    public Argent Montant { get; private set; } = null!;
    public string Libelle { get; private set; } = "";

    private Mouvement() { }  // pour EF Core

    internal Mouvement(DateTime date, TypeMouvement type, Argent montant, string libelle) =>
        (Date, Type, Montant, Libelle) = (date, type, montant, libelle);
}
