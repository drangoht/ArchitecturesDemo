namespace Mk.Contrats;

/// <summary>
/// Tout plugin implémente IPlugin, plus un ou plusieurs points d'extension (IControleOperation, IRegleFrais, IFormatReleve).
/// Le noyau découvre ces classes par réflexion dans les DLL du dossier « plugins ».
/// </summary>
public interface IPlugin
{
    string Nom { get; }
    string Description { get; }

    /// <summary>Version minimale du noyau dont le plugin a besoin. Un plugin trop récent est refusé au chargement.</summary>
    Version VersionNoyauMinimale => new(1, 0);

    /// <summary>Appelé une fois au chargement, avec les réglages lus dans le plugin.json du plugin.</summary>
    void Initialiser(ContextePlugin contexte) { }
}

/// <summary>Ce que le noyau met à disposition d'un plugin. Le plugin ne voit RIEN d'autre du noyau.</summary>
public sealed class ContextePlugin(IReadOnlyDictionary<string, string> reglages, Action<string> journaliser)
{
    public string? Reglage(string cle) => reglages.GetValueOrDefault(cle);
    public void Journaliser(string message) => journaliser(message);
}

// ─── Les données échangées : de simples valeurs, jamais les objets internes du noyau ───

public enum TypeOperation { Retrait, Virement }

public record OperationProposee(
    TypeOperation Type, Guid CompteId, string Titulaire, decimal Montant, decimal Solde, Guid? CompteCible, DateTime Date);

public record LigneReleve(DateTime Date, string Libelle, decimal Montant, decimal SoldeApres);

public record ReleveCompte(Guid CompteId, string Titulaire, decimal Solde, IReadOnlyList<LigneReleve> Lignes);

// ─── Les points d'extension ───

/// <summary>Peut refuser une opération avant qu'elle ait lieu.</summary>
public interface IControleOperation : IPlugin
{
    /// <returns>null si l'opération est permise, sinon la raison du refus.</returns>
    string? Verifier(OperationProposee operation);
}

/// <summary>Ajoute des frais à une opération.</summary>
public interface IRegleFrais : IPlugin
{
    string Libelle { get; }
    decimal CalculerFrais(OperationProposee operation);
}

/// <summary>Produit un relevé de compte dans un format donné.</summary>
public interface IFormatReleve : IPlugin
{
    string Format { get; }
    string TypeMime { get; }
    string Produire(ReleveCompte releve);
}
