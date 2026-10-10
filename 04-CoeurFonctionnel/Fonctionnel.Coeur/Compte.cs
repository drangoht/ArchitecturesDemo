namespace Fonctionnel.Coeur;

/// <summary>Valeur immuable : un compte n'est jamais modifié, on en produit une nouvelle version.</summary>
public sealed record Compte(Guid Id, string Titulaire, decimal Solde);
