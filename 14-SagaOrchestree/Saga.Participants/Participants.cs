using System.Collections.Concurrent;

namespace Saga.Participants;

/// <summary>Refus définitif (solde insuffisant, compte inconnu…) : réessayer ne servirait à rien.</summary>
public class RefusMetierException(string message) : Exception(message);

/// <summary>Panne passagère (service indisponible…) : réessayer plus tard peut réussir.</summary>
public class PanneException(string message) : Exception(message);

/// <summary>Interrupteurs de la démo pour provoquer des pannes chez les participants.</summary>
public sealed class Chaos
{
    public int EchecsChangeRestants { get; set; }
    public bool ChangeHorsService { get; set; }
    public bool NotificationsHorsService { get; set; }
}

public record CompteVue(Guid Id, string Titulaire, string Devise, decimal Solde, decimal Reserve, decimal Disponible);

/// <summary>
/// Service « Comptes ». Un virement ne débite pas directement : il RÉSERVE d'abord les fonds (ils ne sont plus
/// disponibles mais pas encore partis), puis les CAPTURE à la fin. Une réservation se libère facilement : c'est
/// ce qui rend la compensation simple. Toutes les opérations sont idempotentes (clé : l'identifiant de la saga).
/// </summary>
public sealed class ServiceComptes
{
    private sealed class Compte(Guid id, string titulaire, string devise, decimal solde)
    {
        public Guid Id { get; } = id;
        public string Titulaire { get; } = titulaire;
        public string Devise { get; } = devise;
        public decimal Solde { get; set; } = solde;
        public Dictionary<Guid, decimal> Reservations { get; } = [];
        public Dictionary<Guid, decimal> Credits { get; } = [];
        public decimal Reserve => Reservations.Values.Sum();
        public CompteVue Vue() => new(Id, Titulaire, Devise, Solde, Reserve, Solde - Reserve);
    }

    private readonly Dictionary<Guid, Compte> _comptes = [];
    private readonly Lock _verrou = new();

    public void Ouvrir(Guid id, string titulaire, string devise, decimal solde)
    {
        lock (_verrou)
            _comptes[id] = new Compte(id, titulaire, devise, solde);
    }

    public CompteVue? Consulter(Guid id)
    {
        lock (_verrou)
            return _comptes.GetValueOrDefault(id)?.Vue();
    }

    public IReadOnlyList<CompteVue> Tous()
    {
        lock (_verrou)
            return _comptes.Values.Select(c => c.Vue()).ToList();
    }

    public string Devise(Guid id) => Obtenir(id).Devise;

    public Task Reserver(Guid saga, Guid compteId, decimal montant) => Modifier(compteId, compte =>
    {
        if (compte.Reservations.ContainsKey(saga))
            return;  // déjà réservé par cette saga : rejouer est sans effet
        if (montant > compte.Solde - compte.Reserve)
            throw new RefusMetierException($"Solde disponible insuffisant ({compte.Solde - compte.Reserve:0.00} {compte.Devise}).");
        compte.Reservations[saga] = montant;
    });

    public Task Liberer(Guid saga, Guid compteId) => Modifier(compteId, compte => compte.Reservations.Remove(saga));

    /// <summary>Débit définitif : la réservation devient un vrai débit.</summary>
    public Task Capturer(Guid saga, Guid compteId) => Modifier(compteId, compte =>
    {
        if (compte.Reservations.Remove(saga, out var montant))
            compte.Solde -= montant;
    });

    public Task Crediter(Guid saga, Guid compteId, decimal montant) => Modifier(compteId, compte =>
    {
        if (compte.Credits.TryAdd(saga, montant))
            compte.Solde += montant;
    });

    public Task AnnulerCredit(Guid saga, Guid compteId) => Modifier(compteId, compte =>
    {
        if (compte.Credits.Remove(saga, out var montant))
            compte.Solde -= montant;
    });

    private Compte Obtenir(Guid id)
    {
        lock (_verrou)
            return _comptes.GetValueOrDefault(id) ?? throw new RefusMetierException($"Compte {id} introuvable.");
    }

    private Task Modifier(Guid id, Action<Compte> modification)
    {
        lock (_verrou)
            modification(Obtenir(id));
        return Task.CompletedTask;
    }
}

public enum AvisConformite { Approuve, EnAttenteAnalyste, Refuse }

/// <summary>Service « Conformité » : au-delà d'un seuil, un analyste humain doit décider (réponse asynchrone).</summary>
public sealed class ServiceConformite
{
    public const decimal SeuilAnalyse = 10_000m;

    public Task<AvisConformite> Examiner(Guid saga, decimal montantEnEuros) =>
        Task.FromResult(montantEnEuros >= SeuilAnalyse ? AvisConformite.EnAttenteAnalyste : AvisConformite.Approuve);
}

/// <summary>Service « Change ». La cotation obtenue pour une saga est conservée : la redemander donne le même résultat.</summary>
public sealed class ServiceChange(Chaos chaos)
{
    private static readonly Dictionary<string, decimal> TauxDepuisEuro = new() { ["EUR"] = 1m, ["USD"] = 1.0832m, ["CHF"] = 0.9415m };
    private readonly ConcurrentDictionary<Guid, decimal> _cotations = new();

    public static decimal EnEuros(decimal montant, string devise) => Math.Round(montant / TauxDepuisEuro[devise], 2);

    public Task<decimal> Convertir(Guid saga, decimal montant, string de, string vers)
    {
        if (chaos.ChangeHorsService)
            throw new PanneException("Service de change hors service.");
        if (chaos.EchecsChangeRestants > 0)
        {
            chaos.EchecsChangeRestants--;
            throw new PanneException("Service de change momentanément indisponible.");
        }
        return Task.FromResult(_cotations.GetOrAdd(saga, _ => Math.Round(montant / TauxDepuisEuro[de] * TauxDepuisEuro[vers], 2)));
    }

    public Task AnnulerCotation(Guid saga)
    {
        _cotations.TryRemove(saga, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Service « Notifications ».</summary>
public sealed class ServiceNotifications(Chaos chaos)
{
    private readonly ConcurrentDictionary<Guid, string> _envoyees = new();

    public IReadOnlyCollection<string> Envoyees => _envoyees.Values.ToList();

    public Task Envoyer(Guid saga, string message)
    {
        if (chaos.NotificationsHorsService)
            throw new PanneException("Service de notifications hors service.");
        _envoyees.TryAdd(saga, message);
        return Task.CompletedTask;
    }
}
