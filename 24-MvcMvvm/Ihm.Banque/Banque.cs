namespace Ihm.Banque;

public sealed record Compte(Guid Id, string Titulaire, string Libelle, decimal Solde);

public sealed record Operation(DateTimeOffset Date, string Libelle, decimal Montant);

public sealed record Virement(Guid Source, Guid Cible, decimal Montant, decimal SoldeSource, decimal SoldeCible);

/// <summary>Une règle métier refusée. Le message est fait pour être montré tel quel à l'utilisateur.</summary>
public sealed class ErreurMetier(string message) : Exception(message);

/// <summary>
/// Ce que la banque offre aux écrans. Asynchrone, comme un vrai service distant : c'est ce qui oblige une interface
/// à gérer l'attente (bouton désactivé, indicateur d'activité, réponses qui arrivent dans le désordre).
/// </summary>
public interface IBanque
{
    Task<IReadOnlyList<Compte>> ComptesAsync(CancellationToken annulation = default);

    Task<Compte> CompteAsync(Guid id, CancellationToken annulation = default);

    /// <summary>Les opérations du compte, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<Operation>> OperationsAsync(Guid compte, CancellationToken annulation = default);

    Task<Virement> VirerAsync(Guid source, Guid cible, decimal montant, CancellationToken annulation = default);
}

/// <summary>
/// Les règles d'un virement, publiques pour que l'interface puisse prévenir l'utilisateur AVANT d'envoyer
/// (le ViewModel les appelle à chaque frappe). La banque les applique de nouveau, et c'est elle qui fait foi :
/// entre la saisie et l'envoi, le solde a pu changer.
/// </summary>
public static class ReglesVirement
{
    /// <summary>Au-delà, l'interface demande une confirmation (règle d'ergonomie, pas de la banque).</summary>
    public const decimal SeuilConfirmation = 1_000m;

    public static string? VerifierMontant(decimal montant)
    {
        if (montant <= 0)
            return "Le montant doit être positif.";
        if (decimal.Round(montant, 2) != montant)
            return "Le montant se donne au centime près.";
        return null;
    }

    public static string? VerifierComptes(Guid source, Guid cible) =>
        source == cible ? "Choisissez deux comptes différents." : null;

    public static string? VerifierSolde(decimal solde, decimal montant) =>
        montant > solde ? $"Solde insuffisant : {Format.Euros(solde)} disponibles." : null;
}

/// <summary>Le format des montants, le même dans tous les écrans (et dans les messages d'erreur).</summary>
public static class Format
{
    public static readonly System.Globalization.CultureInfo Francais = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    // Les espaces insécables (fines ou non) de la culture française deviennent des espaces ordinaires :
    // plus simple à lire dans une console, à saisir et à chercher dans un test.
    public static string Euros(decimal montant) =>
        montant.ToString("#,##0.00 €", Francais).Replace(' ', ' ').Replace(' ', ' ');

    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    /// <summary>Une date, à l'heure de Paris quel que soit le fuseau où elle a été enregistrée.</summary>
    public static string Date(DateTimeOffset date) =>
        TimeZoneInfo.ConvertTime(date, Paris).ToString("dd/MM/yyyy HH:mm", Francais);
}
