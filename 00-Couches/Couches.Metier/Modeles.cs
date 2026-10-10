namespace Couches.Metier;

/// <summary>Ce que la couche métier renvoie à la présentation (jamais l'entité de la couche données).</summary>
public record CompteDto(Guid Id, string Titulaire, decimal Solde);

public class RegleMetierException(string message) : Exception(message);

public class CompteIntrouvableException(Guid compteId) : Exception($"Compte {compteId} introuvable.");
