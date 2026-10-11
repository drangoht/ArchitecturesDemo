using System.ComponentModel.DataAnnotations;
using Ihm.Banque;

namespace Ihm.Mvc.Models;

// Les données que le contrôleur passe à chaque vue. Dans le vocabulaire d'ASP.NET Core, on les appelle souvent
// des « view models » : ce ne sont PAS les ViewModels de MVVM (qui ont un état, des commandes et vivent avec la vue),
// juste des sacs de données préparés pour un rendu, puis jetés.

public sealed record LigneCompte(Guid Id, string Titulaire, string Libelle, string Solde);

public sealed record ListeComptes(IReadOnlyList<LigneCompte> Comptes);

public sealed record LigneOperation(string Date, string Libelle, string Montant, bool EstDebit);

public sealed record DetailCompte(LigneCompte Compte, IReadOnlyList<LigneOperation> Operations);

/// <summary>
/// Le formulaire de virement, dans les deux sens : rempli par la liaison de modèle à partir des champs envoyés,
/// renvoyé à la vue (avec les erreurs) quand il faut le réafficher.
/// </summary>
public sealed class FormulaireVirement : IValidatableObject
{
    [Required(ErrorMessage = "Choisissez le compte à débiter.")]
    [Display(Name = "Compte à débiter")]
    public Guid? Source { get; set; }

    [Required(ErrorMessage = "Choisissez le compte à créditer.")]
    [Display(Name = "Compte à créditer")]
    public Guid? Cible { get; set; }

    [Required(ErrorMessage = "Saisissez un montant.")]
    [Display(Name = "Montant (€)")]
    public decimal? Montant { get; set; }

    /// <summary>Vrai quand l'utilisateur a validé l'écran de confirmation (gros montants).</summary>
    public bool Confirme { get; set; }

    /// <summary>Les comptes proposés dans les listes : pas envoyés par le navigateur, rechargés par le contrôleur.</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public IReadOnlyList<LigneCompte> Comptes { get; set; } = [];

    // Règles qui portent sur plusieurs champs : appelées par la validation de MVC, après les attributs.
    // Ce sont les mêmes que celles de la banque, pour prévenir AVANT l'appel ; la banque les vérifie de nouveau.
    public IEnumerable<ValidationResult> Validate(ValidationContext contexte)
    {
        if (Montant is { } montant && ReglesVirement.VerifierMontant(montant) is { } erreurMontant)
            yield return new ValidationResult(erreurMontant, [nameof(Montant)]);
        if (Source is { } source && Cible is { } cible && ReglesVirement.VerifierComptes(source, cible) is { } erreurComptes)
            yield return new ValidationResult(erreurComptes, [nameof(Cible)]);
    }
}
