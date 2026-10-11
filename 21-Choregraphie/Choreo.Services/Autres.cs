using Choreo.Bus;
using Choreo.Services.Contrats;

namespace Choreo.Services
{
    /// <summary>Les taux de change (depuis l'euro), connus du service Change et, pour les montants en euros, de Comptes.</summary>
    public static class Taux
    {
        public static readonly IReadOnlyDictionary<string, decimal> DepuisEuro =
            new Dictionary<string, decimal> { ["EUR"] = 1m, ["USD"] = 1.0832m, ["CHF"] = 0.9415m };

        /// <summary>Une devise non cotée est comptée à part égale : c'est au service Change de la refuser.</summary>
        public static decimal EnEuros(decimal montant, string devise) =>
            DepuisEuro.TryGetValue(devise, out var taux) ? Math.Round(montant / taux, 2) : montant;
    }

    /// <summary>
    /// Le motif commun à tous les services : réagir une seule fois à un événement (la « boîte de réception » : un événement
    /// livré deux fois n'agit qu'une fois), et changer l'état ET publier la suite d'un seul tenant (sinon : l'outbox de la démo 8).
    /// </summary>
    public abstract class Reactif(Choreo.Bus.Bus bus, string nom)
    {
        private readonly HashSet<Guid> _traites = new();
        protected readonly Lock Verrou = new();
        protected Choreo.Bus.Bus Bus { get; } = bus;
        public string Nom { get; } = nom;

        protected void UneSeuleFois(Evenement recu, Func<Evenement?> reagir)
        {
            lock (Verrou)
            {
                if (_traites.Contains(recu.Id))
                    return;
                if (reagir() is { } suite)
                    Bus.Publier(suite);
                _traites.Add(recu.Id);
            }
        }
    }
}

namespace Choreo.Services.Conformite
{
    /// <summary>
    /// La conformité : liste noire, et au-delà de 10 000 € un analyste humain décide (plus tard, par l'API).
    /// Elle écoute aussi l'expiration des réservations : inutile de faire décider un analyste sur un virement abandonné.
    /// </summary>
    public sealed class ServiceConformite(Choreo.Bus.Bus bus) : Reactif(bus, "Conformité")
    {
        public const decimal SeuilAnalyse = 10_000m;
        public static readonly Guid Mallory = Guid.Parse("66666666-6666-6666-6666-666666666666");

        private readonly Dictionary<string, ConformiteEnAttente> _enAttente = new();

        public void Inscrire()
        {
            Bus.Abonner<FondsReserves>(Nom, Examiner);
            Bus.Abonner<ReservationExpiree>(Nom, e => UneSeuleFois(e, () =>
            {
                _enAttente.Remove(e.Correlation);
                return null;
            }));
        }

        private void Examiner(FondsReserves e) => UneSeuleFois(e, () =>
        {
            if (e.Ordre.Cible == Mallory)
                return e.Suite(new ConformiteRefusee(e.Ordre, "bénéficiaire sur liste noire"), Nom);
            if (e.Ordre.MontantEur < SeuilAnalyse)
                return e.Suite(new ConformiteApprouvee(e.Ordre), Nom);
            var attente = e.Suite(new ConformiteEnAttente(e.Ordre), Nom);
            _enAttente[e.Correlation] = attente;
            return attente;
        });

        public IReadOnlyList<string> EnAttente()
        {
            lock (Verrou) return _enAttente.Keys.Order().ToList();
        }

        /// <summary>La décision d'un analyste : un événement venu d'un humain, en réponse à ConformiteEnAttente.</summary>
        public bool Decider(string virement, bool approuve)
        {
            lock (Verrou)
            {
                if (!_enAttente.Remove(virement, out var attente))
                    return false;
                Bus.Publier<EvenementVirement>(approuve
                    ? attente.Suite(new ConformiteApprouvee(attente.Ordre), Nom)
                    : attente.Suite(new ConformiteRefusee(attente.Ordre, "refusé par un analyste"), Nom));
                return true;
            }
        }
    }
}

namespace Choreo.Services.Change
{
    /// <summary>Le change : il convertit le montant dans la devise du bénéficiaire. Peut être mis en panne (exception).</summary>
    public sealed class ServiceChange(Choreo.Bus.Bus bus) : Reactif(bus, "Change")
    {
        public volatile bool EnPanne;

        public void Inscrire() => Bus.Abonner<ConformiteApprouvee>(Nom, Convertir);

        private void Convertir(ConformiteApprouvee e)
        {
            if (EnPanne)
                throw new InvalidOperationException("Service de change indisponible.");
            UneSeuleFois(e, () =>
            {
                var o = e.Ordre;
                if (!Taux.DepuisEuro.TryGetValue(o.DeviseSource, out var de) || !Taux.DepuisEuro.TryGetValue(o.DeviseCible, out var vers))
                    return e.Suite(new ChangeEchoue(o, $"devise non cotée ({o.DeviseSource} → {o.DeviseCible})"), Nom);
                return e.Suite(new DevisesConverties(o with { MontantCible = Math.Round(o.Montant / de * vers, 2) }), Nom);
            });
        }
    }
}

namespace Choreo.Services.Notifications
{
    /// <summary>Prévient le client une fois le virement réglé. Après le point de non-retour : en cas d'échec, on réessaie, on ne défait rien.</summary>
    public sealed class ServiceNotifications(Choreo.Bus.Bus bus) : Reactif(bus, "Notifications")
    {
        private readonly List<string> _envoyees = [];
        private int _pannesRestantes;

        public void Inscrire() => Bus.Abonner<VirementRegle>(Nom, Notifier);

        /// <summary>Les N prochaines tentatives échoueront (serveur de courriel indisponible).</summary>
        public void Saboter(int pannes) => Interlocked.Exchange(ref _pannesRestantes, pannes);

        private void Notifier(VirementRegle e)
        {
            if (Interlocked.Decrement(ref _pannesRestantes) >= 0)
                throw new InvalidOperationException("Serveur de courriel indisponible.");
            UneSeuleFois(e, () =>
            {
                var message = $"Virement de {e.Ordre.Montant:0.00} {e.Ordre.DeviseSource} effectué ({e.Ordre.MontantCible:0.00} {e.Ordre.DeviseCible} reçus).";
                _envoyees.Add(message);
                return e.Suite(new ClientNotifie(e.Ordre, message), Nom);
            });
        }

        public IReadOnlyList<string> Envoyees()
        {
            lock (Verrou) return _envoyees.ToList();
        }
    }
}

namespace Choreo.Services.Fidelite
{
    /// <summary>
    /// Le service ajouté APRÈS COUP : des points de fidélité sur chaque virement réglé. Il suffit de s'abonner ;
    /// aucun autre service n'a été modifié, aucun ne sait qu'il existe.
    /// </summary>
    public sealed class ServiceFidelite(Choreo.Bus.Bus bus) : Reactif(bus, "Fidélité")
    {
        private readonly Dictionary<Guid, int> _points = new();

        public void Inscrire() => Bus.Abonner<VirementRegle>(Nom, e => UneSeuleFois(e, () =>
        {
            _points[e.Ordre.Source] = _points.GetValueOrDefault(e.Ordre.Source) + (int)(e.Ordre.MontantEur / 10);
            return null;
        }));

        public int Points(Guid compte)
        {
            lock (Verrou) return _points.GetValueOrDefault(compte);
        }
    }
}
