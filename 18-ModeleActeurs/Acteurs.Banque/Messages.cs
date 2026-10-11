namespace Acteurs.Banque;

// Les messages sont des records immuables : une fois déposés, ni l'expéditeur ni le destinataire ne peuvent les modifier.
// C'est le seul moyen d'échanger des données entre acteurs, aucun état n'est partagé.

// ── Messages d'un compte ──
public sealed record OuvrirCompte(string Titulaire, decimal DepotInitial);
public sealed record ConsulterCompte;
public sealed record ListerOperations;
public sealed record Deposer(decimal Montant);
public sealed record Retirer(decimal Montant);

/// <summary>Débit demandé par un virement : la <paramref name="Cle"/> rend la répétition sans effet (idempotence).</summary>
public sealed record Debiter(string Cle, decimal Montant, string Libelle);

/// <summary>Crédit demandé par un virement, idempotent lui aussi.</summary>
public sealed record Crediter(string Cle, decimal Montant, string Libelle);

// ── Messages d'un virement ──
public sealed record ExecuterVirement(Guid Source, Guid Cible, decimal Montant);
public sealed record ConsulterVirement;

// ── Réponses ──
public sealed record CompteDto(Guid Id, string Titulaire, decimal Solde);
public sealed record OperationDto(DateTimeOffset Date, string Libelle, decimal Montant);
public sealed record VirementDto(string Id, Guid Source, Guid Cible, decimal Montant, StatutVirement Statut, string? Motif);

/// <summary>Un refus métier : une réponse normale, pas une panne (l'acteur continue tranquillement).</summary>
public sealed record Refus(string Motif, bool Introuvable = false);

public enum StatutVirement { EnCours, DebitFait, Termine, Refuse, Annule }
