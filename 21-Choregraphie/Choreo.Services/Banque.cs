using Choreo.Bus;
using Choreo.Services.Change;
using Choreo.Services.Comptes;
using Choreo.Services.Conformite;
using Choreo.Services.Contrats;
using Choreo.Services.Fidelite;
using Choreo.Services.Notifications;
using Choreo.Services.Suivi;

namespace Choreo.Services;

public sealed record OptionsBanque
{
    public TimeSpan DureeReservation { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Le service ajouté après coup : on peut le brancher ou non, les autres n'en savent rien.</summary>
    public bool Fidelite { get; init; } = true;

    public OptionsBus Bus { get; init; } = new();
}

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Yuki = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Mallory = ServiceConformite.Mallory;
}

/// <summary>
/// L'assemblage : un bus, des services qui s'y abonnent. Ce n'est PAS un orchestrateur : après le premier événement,
/// il ne fait plus rien ; il sert seulement à démarrer le système et à lancer des virements.
/// </summary>
public sealed class Banque : IAsyncDisposable
{
    private Banque(OptionsBanque options, TimeProvider horloge)
    {
        Horloge = horloge;
        Bus = new Choreo.Bus.Bus(options.Bus, horloge);
        Comptes = new ServiceComptes(Bus, horloge, options.DureeReservation);
        Conformite = new ServiceConformite(Bus);
        Change = new ServiceChange(Bus);
        Notifications = new ServiceNotifications(Bus);
        Fidelite = options.Fidelite ? new ServiceFidelite(Bus) : null;
        Suivi = new ServiceSuivi(Bus);
    }

    public TimeProvider Horloge { get; }
    public Choreo.Bus.Bus Bus { get; }
    public ServiceComptes Comptes { get; }
    public ServiceConformite Conformite { get; }
    public ServiceChange Change { get; }
    public ServiceNotifications Notifications { get; }
    public ServiceFidelite? Fidelite { get; }
    public ServiceSuivi Suivi { get; }

    public static Banque Demarrer(OptionsBanque? options = null, TimeProvider? horloge = null)
    {
        var banque = new Banque(options ?? new OptionsBanque(), horloge ?? TimeProvider.System);
        banque.Suivi.Inscrire();
        banque.Comptes.Inscrire();
        banque.Conformite.Inscrire();
        banque.Change.Inscrire();
        banque.Notifications.Inscrire();
        banque.Fidelite?.Inscrire();

        banque.Comptes.Ouvrir(DonneesDeDemo.Alice, "Alice", "EUR", 50_000m);
        banque.Comptes.Ouvrir(DonneesDeDemo.Bob, "Bob", "EUR", 500m);
        banque.Comptes.Ouvrir(DonneesDeDemo.Carla, "Carla", "USD", 1_000m);
        banque.Comptes.Ouvrir(DonneesDeDemo.Yuki, "Yuki", "JPY", 100_000m);
        banque.Comptes.Ouvrir(DonneesDeDemo.Mallory, "Mallory", "EUR", 0m);
        return banque;
    }

    /// <summary>Le seul geste « de l'extérieur » : publier qu'un virement est demandé. La suite ne dépend plus de personne.</summary>
    public string Demander(Guid source, Guid cible, decimal montant, string? id = null)
    {
        var virement = id ?? $"v-{Guid.NewGuid():N}"[..10];
        Bus.Publier(new VirementDemande(new Ordre(source, cible, montant)) { Correlation = virement, Emetteur = "API" });
        return virement;
    }

    /// <summary>Attend que le virement soit terminé (ou en attente d'un analyste), d'après le Suivi.</summary>
    public async Task<EtatVirement?> Attendre(string id, TimeSpan? delai = null)
    {
        var limite = DateTime.UtcNow + (delai ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < limite)
        {
            if (Suivi.Etat(id) is { } etat && (etat.EstTermine || etat.Statut == StatutVirement.EnAttenteAnalyste))
                return etat;
            await Task.Delay(5);
        }
        return Suivi.Etat(id);
    }

    public ValueTask DisposeAsync() => Bus.DisposeAsync();
}
