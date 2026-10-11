using Choreo.Bus;
using Choreo.Services.Contrats;

namespace Choreo.Services.Suivi;

public enum StatutVirement { EnCours, EnAttenteAnalyste, AnnulationEnCours, Regle, Refuse, Annule, Abandonne }

public sealed record EtatVirement(
    string Id,
    Ordre Ordre,
    StatutVirement Statut,
    string DerniereEtape,
    DateTimeOffset DerniereActivite,
    string? Motif,
    bool ClientNotifie)
{
    public bool EstTermine => Statut is StatutVirement.Regle or StatutVirement.Refuse or StatutVirement.Annule or StatutVirement.Abandonne;
}

/// <summary>
/// En chorégraphie, personne ne sait « où en est le virement 42 ». Ce service le reconstitue en écoutant TOUS les
/// événements du processus (une projection, comme en CQRS). Il ne décide rien ; il observe. Revers : il doit connaître
/// chaque type d'événement, et chaque nouvelle étape du processus doit lui être ajoutée.
/// </summary>
public sealed class ServiceSuivi(Choreo.Bus.Bus bus) : Reactif(bus, "Suivi")
{
    private readonly Dictionary<string, EtatVirement> _virements = new();

    public void Inscrire()
    {
        Ecouter<VirementDemande>(StatutVirement.EnCours);
        Ecouter<FondsReserves>(StatutVirement.EnCours);
        Ecouter<FondsRefuses>(StatutVirement.Refuse, e => e.Motif);
        Ecouter<ConformiteEnAttente>(StatutVirement.EnAttenteAnalyste);
        Ecouter<ConformiteApprouvee>(StatutVirement.EnCours);
        Ecouter<ConformiteRefusee>(StatutVirement.AnnulationEnCours, e => e.Motif);
        Ecouter<DevisesConverties>(StatutVirement.EnCours);
        Ecouter<ChangeEchoue>(StatutVirement.AnnulationEnCours, e => e.Motif);
        Ecouter<FondsLiberes>(StatutVirement.Annule, e => e.Motif);
        Ecouter<VirementRegle>(StatutVirement.Regle);
        Ecouter<ReservationExpiree>(StatutVirement.Abandonne, _ => "réservation expirée : personne n'a conclu à temps");
        Ecouter<VirementAbandonne>(StatutVirement.Abandonne, e => e.Motif);
        Bus.Abonner<ClientNotifie>(Nom, e => UneSeuleFois(e, () =>
        {
            if (_virements.TryGetValue(e.Correlation, out var v))
                _virements[e.Correlation] = v with { ClientNotifie = true, DerniereActivite = e.Date };
            return null;
        }));
    }

    private void Ecouter<T>(StatutVirement statut, Func<T, string>? motif = null) where T : EvenementVirement =>
        Bus.Abonner<T>(Nom, e => UneSeuleFois(e, () =>
        {
            var precedent = _virements.GetValueOrDefault(e.Correlation);
            // Un virement terminé le reste : un événement tardif (un règlement après expiration) ne le rouvre pas.
            var nouveauStatut = precedent is { EstTermine: true } && statut != StatutVirement.Abandonne ? precedent.Statut : statut;
            _virements[e.Correlation] = new EtatVirement(e.Correlation, e.Ordre, nouveauStatut, e.Type, e.Date,
                motif?.Invoke(e) ?? precedent?.Motif, precedent?.ClientNotifie ?? false);
            return null;
        }));

    public EtatVirement? Etat(string id)
    {
        lock (Verrou) return _virements.GetValueOrDefault(id);
    }

    public IReadOnlyList<EtatVirement> Tous()
    {
        lock (Verrou) return _virements.Values.OrderBy(v => v.DerniereActivite).ToList();
    }

    /// <summary>Les virements en cours sans le moindre événement depuis <paramref name="delai"/> : quelque chose s'est perdu en route.</summary>
    public IReadOnlyList<EtatVirement> Bloques(DateTimeOffset maintenant, TimeSpan delai)
    {
        lock (Verrou)
            return _virements.Values
                .Where(v => v.Statut is StatutVirement.EnCours or StatutVirement.AnnulationEnCours && maintenant - v.DerniereActivite > delai)
                .ToList();
    }
}
