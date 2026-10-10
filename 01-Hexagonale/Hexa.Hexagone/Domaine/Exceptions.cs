namespace Hexa.Hexagone.Domaine;

/// <summary>Une règle métier a été violée (solde insuffisant, montant invalide…).</summary>
public class RegleMetierException(string message) : Exception(message);

/// <summary>Le compte demandé n'existe pas.</summary>
public class CompteIntrouvableException(Guid compteId) : Exception($"Compte {compteId} introuvable.")
{
    public Guid CompteId { get; } = compteId;
}
