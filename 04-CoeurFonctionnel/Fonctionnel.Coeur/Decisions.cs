namespace Fonctionnel.Coeur;

// Les règles ne FONT rien : elles renvoient une décision que la coquille appliquera.
// Chaque décision est une petite hiérarchie fermée (Acceptée / Refusée), exploitable par pattern matching.

public abstract record DecisionRetrait
{
    public sealed record Accepte(Compte CompteMisAJour, decimal Montant) : DecisionRetrait;
    public sealed record Refuse(string Raison) : DecisionRetrait;
}

public abstract record DecisionVirement
{
    public sealed record Accepte(Compte Source, Compte Cible, decimal Montant) : DecisionVirement;
    public sealed record Refuse(string Raison) : DecisionVirement;
}
