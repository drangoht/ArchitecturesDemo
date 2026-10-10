using Modulaire.Partage;

namespace Modulaire.Comptes.Contrats;

// ─── Communication synchrone : une API que les autres modules peuvent appeler ───

public interface IComptesApi
{
    Task<CompteInfo?> Consulter(Guid compteId);
    Task<ResultatOperation> Debiter(Guid compteId, decimal montant);
    Task<ResultatOperation> Crediter(Guid compteId, decimal montant);
}

public record CompteInfo(Guid Id, string Titulaire, decimal Solde);

public enum StatutOperation { Ok, Refuse, Introuvable }

public record ResultatOperation(StatutOperation Statut, string? Erreur = null)
{
    public bool Succes => Statut == StatutOperation.Ok;

    public static ResultatOperation Ok() => new(StatutOperation.Ok);
    public static ResultatOperation Refuse(string raison) => new(StatutOperation.Refuse, raison);
    public static ResultatOperation Introuvable(Guid id) => new(StatutOperation.Introuvable, $"Compte {id} introuvable.");
}

// ─── Communication asynchrone : les événements que le module publie ───

public record RetraitEffectue(Guid CompteId, decimal Montant, decimal NouveauSolde) : IEvenementIntegration;
