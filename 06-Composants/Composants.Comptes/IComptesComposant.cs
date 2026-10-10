namespace Composants.Comptes;

// ─── Surface publique du composant : c'est tout ce que le reste de l'application peut voir. ───

public interface IComptesComposant
{
    Task<CompteDto> Ouvrir(string titulaire, decimal depotInitial, Guid? id = null);
    Task<CompteDto?> Consulter(Guid compteId);
    Task<Resultat> Retirer(Guid compteId, decimal montant);
    Task<Resultat> Deposer(Guid compteId, decimal montant);
}

public record CompteDto(Guid Id, string Titulaire, decimal Solde);

public enum StatutResultat { Ok, Refuse, Introuvable }

public record Resultat(StatutResultat Statut, string? Erreur = null)
{
    public bool Succes => Statut == StatutResultat.Ok;

    public static Resultat Ok() => new(StatutResultat.Ok);
    public static Resultat Refuse(string raison) => new(StatutResultat.Refuse, raison);
    public static Resultat Introuvable(Guid compteId) => new(StatutResultat.Introuvable, $"Compte {compteId} introuvable.");
}
