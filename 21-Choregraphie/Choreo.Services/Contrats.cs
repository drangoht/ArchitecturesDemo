using Choreo.Bus;

namespace Choreo.Services.Contrats;

// Le SEUL code partagé entre services : la forme des événements. Changer un contrat, c'est prévenir tous ses abonnés…
// que l'émetteur ne connaît pas. (En vrai : des schémas versionnés, un registre de schémas.)

/// <summary>
/// Les données du virement, recopiées dans CHAQUE événement et complétées au fil de l'eau : chaque service trouve ce
/// qu'il lui faut dans l'événement reçu, sans rappeler personne (« event-carried state transfer »).
/// </summary>
public sealed record Ordre(Guid Source, Guid Cible, decimal Montant)
{
    public string DeviseSource { get; init; } = "";
    public string DeviseCible { get; init; } = "";
    public decimal MontantEur { get; init; }
    public decimal MontantCible { get; init; }
}

public abstract record EvenementVirement(Ordre Ordre) : Evenement;

public sealed record VirementDemande(Ordre Ordre) : EvenementVirement(Ordre);

public sealed record FondsReserves(Ordre Ordre) : EvenementVirement(Ordre);
public sealed record FondsRefuses(Ordre Ordre, string Motif) : EvenementVirement(Ordre);

public sealed record ConformiteApprouvee(Ordre Ordre) : EvenementVirement(Ordre);
public sealed record ConformiteEnAttente(Ordre Ordre) : EvenementVirement(Ordre);
public sealed record ConformiteRefusee(Ordre Ordre, string Motif) : EvenementVirement(Ordre);

public sealed record DevisesConverties(Ordre Ordre) : EvenementVirement(Ordre);
public sealed record ChangeEchoue(Ordre Ordre, string Motif) : EvenementVirement(Ordre);

/// <summary>Le point de non-retour : la cible est créditée, la source débitée pour de bon.</summary>
public sealed record VirementRegle(Ordre Ordre) : EvenementVirement(Ordre);

/// <summary>Compensation faite : la réservation est libérée après un refus ou un échec en aval.</summary>
public sealed record FondsLiberes(Ordre Ordre, string Motif) : EvenementVirement(Ordre);

/// <summary>Personne n'a conclu à temps : le service Comptes a libéré la réservation de lui-même.</summary>
public sealed record ReservationExpiree(Ordre Ordre) : EvenementVirement(Ordre);

/// <summary>Un règlement arrivé APRÈS l'expiration de la réservation : refusé.</summary>
public sealed record VirementAbandonne(Ordre Ordre, string Motif) : EvenementVirement(Ordre);

public sealed record ClientNotifie(Ordre Ordre, string Message) : EvenementVirement(Ordre);
