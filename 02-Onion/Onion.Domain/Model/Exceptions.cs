namespace Onion.Domain.Model;

public class RegleMetierException(string message) : Exception(message);

public class CompteIntrouvableException(Guid compteId) : Exception($"Compte {compteId} introuvable.");
