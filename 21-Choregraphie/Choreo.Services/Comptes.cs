using Choreo.Bus;
using Choreo.Services.Contrats;

namespace Choreo.Services.Comptes;

public sealed record CompteDto(Guid Id, string Titulaire, string Devise, decimal Solde, decimal Reserve);

/// <summary>
/// Le service Comptes : il réserve, règle, libère. Il doit SAVOIR quels événements des autres signifient « défais ce que
/// tu as fait » (un refus de la conformité, un échec du change) : la compensation est dispersée dans les services.
/// Et comme personne ne pilote le processus, c'est lui qui se protège d'un processus resté en plan : sa réservation expire.
/// </summary>
public sealed class ServiceComptes(Choreo.Bus.Bus bus, TimeProvider horloge, TimeSpan dureeReservation) : Reactif(bus, "Comptes")
{
    private sealed record Reservation(Ordre Ordre, DateTimeOffset ExpireLe);

    private readonly Dictionary<Guid, (string Titulaire, string Devise, decimal Solde, decimal Reserve)> _comptes = new();
    private readonly Dictionary<string, Reservation> _reservations = new();
    private readonly HashSet<string> _regles = new();

    public void Inscrire()
    {
        Bus.Abonner<VirementDemande>(Nom, Reserver);
        Bus.Abonner<ConformiteRefusee>(Nom, e => Liberer(e, e.Motif));  // compensation
        Bus.Abonner<ChangeEchoue>(Nom, e => Liberer(e, e.Motif));       // compensation
        Bus.Abonner<DevisesConverties>(Nom, Regler);
    }

    public void Ouvrir(Guid id, string titulaire, string devise, decimal solde)
    {
        lock (Verrou) _comptes[id] = (titulaire, devise, solde, 0m);
    }

    public CompteDto? Consulter(Guid id)
    {
        lock (Verrou)
            return _comptes.TryGetValue(id, out var c) ? new CompteDto(id, c.Titulaire, c.Devise, c.Solde, c.Reserve) : null;
    }

    private void Reserver(VirementDemande e) => UneSeuleFois(e, () =>
    {
        var o = e.Ordre;
        if (!_comptes.TryGetValue(o.Source, out var source) || !_comptes.TryGetValue(o.Cible, out var cible))
            return e.Suite(new FondsRefuses(o, "compte inconnu"), Nom);
        if (o.Montant <= 0 || o.Source == o.Cible)
            return e.Suite(new FondsRefuses(o, "virement invalide"), Nom);
        if (source.Solde - source.Reserve < o.Montant)
            return e.Suite(new FondsRefuses(o, $"solde disponible insuffisant ({source.Solde - source.Reserve:0.00} {source.Devise})"), Nom);

        _comptes[o.Source] = source with { Reserve = source.Reserve + o.Montant };
        // Le service enrichit l'ordre de ce qu'il est seul à savoir : les devises des deux comptes.
        var enrichi = o with { DeviseSource = source.Devise, DeviseCible = cible.Devise, MontantEur = Taux.EnEuros(o.Montant, source.Devise) };
        _reservations[e.Correlation] = new Reservation(enrichi, horloge.GetUtcNow() + dureeReservation);
        return e.Suite(new FondsReserves(enrichi), Nom);
    });

    private void Liberer(EvenementVirement e, string motif) => UneSeuleFois(e, () =>
    {
        if (!_reservations.Remove(e.Correlation, out var reservation))
            return null;  // déjà libérée (expirée) : rien à défaire
        Rendre(reservation.Ordre);
        return e.Suite(new FondsLiberes(reservation.Ordre, motif), Nom);
    });

    private void Regler(DevisesConverties e) => UneSeuleFois(e, () =>
    {
        if (!_reservations.Remove(e.Correlation, out var reservation))
            return _regles.Contains(e.Correlation) ? null
                : e.Suite(new VirementAbandonne(e.Ordre, "la réservation a expiré avant le règlement"), Nom);

        var o = e.Ordre;
        var source = _comptes[o.Source];
        var cible = _comptes[o.Cible];
        _comptes[o.Source] = source with { Solde = source.Solde - reservation.Ordre.Montant, Reserve = source.Reserve - reservation.Ordre.Montant };
        _comptes[o.Cible] = cible with { Solde = cible.Solde + o.MontantCible };
        _regles.Add(e.Correlation);
        return e.Suite(new VirementRegle(o), Nom);
    });

    /// <summary>
    /// Appelé régulièrement : les réservations trop anciennes sont libérées. C'est le filet de sécurité de la chorégraphie :
    /// sans lui, un service en panne ou un analyste qui ne répond jamais bloquerait l'argent d'Alice pour toujours.
    /// </summary>
    public int ExpirerReservations()
    {
        List<ReservationExpiree> expirees;
        lock (Verrou)
        {
            var maintenant = horloge.GetUtcNow();
            expirees = _reservations.Where(r => r.Value.ExpireLe <= maintenant).ToList()
                .Select(r =>
                {
                    _reservations.Remove(r.Key);
                    Rendre(r.Value.Ordre);
                    return new ReservationExpiree(r.Value.Ordre) { Correlation = r.Key, Emetteur = Nom };
                }).ToList();
            foreach (var e in expirees)
                Bus.Publier(e);
        }
        return expirees.Count;
    }

    private void Rendre(Ordre o)
    {
        var source = _comptes[o.Source];
        _comptes[o.Source] = source with { Reserve = source.Reserve - o.Montant };
    }
}
