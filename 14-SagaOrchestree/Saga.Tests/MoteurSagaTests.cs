using Saga.Orchestrateur;
using Saga.Participants;

namespace Saga.Tests;

/// <summary>Une horloge qu'on avance à la main : les tests de nouveaux essais et d'échéance sont instantanés et déterministes.</summary>
public sealed class HorlogeManuelle : TimeProvider
{
    private DateTimeOffset _maintenant = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _maintenant;
    public void Avancer(TimeSpan duree) => _maintenant += duree;
}

public class MoteurSagaTests
{
    private static readonly Guid Alice = Guid.NewGuid(), Bob = Guid.NewGuid(), Carla = Guid.NewGuid();

    private readonly Chaos _chaos = new();
    private readonly ServiceComptes _comptes = new();
    private readonly ServiceNotifications _notifications;
    private readonly MagasinSagas _magasin = new();
    private readonly HorlogeManuelle _horloge = new();
    private readonly OptionsSaga _options = new() { PauseEntreEtapesMs = 0, AttenteInitialeMs = 100, DelaiDecisionConformiteSecondes = 30 };
    private readonly DefinitionVirement _definition;

    public MoteurSagaTests()
    {
        _notifications = new ServiceNotifications(_chaos);
        _definition = new DefinitionVirement(_comptes, new ServiceConformite(), new ServiceChange(_chaos), _notifications, _options);
        _comptes.Ouvrir(Alice, "Alice", "EUR", 50_000m);
        _comptes.Ouvrir(Bob, "Bob", "EUR", 500m);
        _comptes.Ouvrir(Carla, "Carla", "USD", 1_000m);
    }

    private MoteurSaga NouveauMoteur() => new(_magasin, _definition, _horloge, _options);

    /// <summary>Fait tourner le moteur (en avançant le temps) jusqu'à ce que la saga soit finie ou en attente.</summary>
    private async Task<EtatSaga> Executer(MoteurSaga moteur, Guid id)
    {
        for (var i = 0; i < 200; i++)
        {
            var saga = _magasin.Charger(id)!;
            if (saga.EstFinie || saga.Statut == StatutSaga.EnAttente)
                return saga;
            await moteur.Traiter();
            _horloge.Avancer(TimeSpan.FromMilliseconds(100));
        }
        throw new InvalidOperationException("La saga ne s'est pas stabilisée.");
    }

    private decimal Solde(Guid id) => _comptes.Consulter(id)!.Solde;
    private decimal Disponible(Guid id) => _comptes.Consulter(id)!.Disponible;
    private static IEnumerable<string> Messages(EtatSaga saga) => saga.Journal.Select(j => j.Message);

    [Fact]
    public async Task Un_virement_passe_par_les_six_etapes_dans_l_ordre()
    {
        var moteur = NouveauMoteur();

        var saga = await Executer(moteur, moteur.Demarrer(Alice, Bob, 100m).Id);

        Assert.Equal(StatutSaga.Terminee, saga.Statut);
        Assert.Equal(["ReserverFonds", "ControlerConformite", "ConvertirDevises", "CrediterBeneficiaire", "DebiterDefinitivement", "Notifier"],
            saga.EtapesFaites);
        Assert.Equal((49_900m, 600m), (Solde(Alice), Solde(Bob)));
        Assert.Single(_notifications.Envoyees);
    }

    [Fact]
    public async Task Un_virement_vers_un_compte_en_dollars_est_converti()
    {
        var moteur = NouveauMoteur();

        var saga = await Executer(moteur, moteur.Demarrer(Alice, Carla, 100m).Id);

        Assert.Equal((108.32m, "USD"), (saga.MontantCredite, saga.DeviseCible));
        Assert.Equal(1_108.32m, Solde(Carla));
    }

    [Fact]
    public async Task Un_refus_a_la_premiere_etape_annule_sans_rien_a_defaire()
    {
        var moteur = NouveauMoteur();

        var saga = await Executer(moteur, moteur.Demarrer(Bob, Alice, 9_999m).Id);

        Assert.Equal(StatutSaga.Annulee, saga.Statut);
        Assert.Contains("Solde disponible insuffisant", saga.Raison);
        Assert.Equal(500m, Solde(Bob));
    }

    [Fact]
    public async Task Une_panne_passagere_est_absorbee_par_de_nouveaux_essais()
    {
        _chaos.EchecsChangeRestants = 2;
        var moteur = NouveauMoteur();

        var saga = await Executer(moteur, moteur.Demarrer(Alice, Carla, 100m).Id);

        Assert.Equal(StatutSaga.Terminee, saga.Statut);
        Assert.Equal(2, Messages(saga).Count(m => m.StartsWith("✖ ConvertirDevises")));
    }

    [Fact]
    public async Task Une_panne_durable_declenche_la_compensation_dans_l_ordre_inverse()
    {
        _chaos.ChangeHorsService = true;
        var moteur = NouveauMoteur();

        var saga = await Executer(moteur, moteur.Demarrer(Alice, Carla, 100m).Id);

        Assert.Equal(StatutSaga.Annulee, saga.Statut);
        Assert.Equal(3, Messages(saga).Count(m => m.StartsWith("✖ ConvertirDevises")));  // 3 nouveaux essais…
        Assert.Contains("impossible après 4 tentatives", saga.Raison);                      // …puis abandon au 4e échec
        Assert.Contains("↩ ReserverFonds défaite", Messages(saga));
        Assert.Equal(50_000m, Disponible(Alice));  // la réservation a été libérée
        Assert.Equal(1_000m, Solde(Carla));
    }

    [Theory]
    [InlineData(true, StatutSaga.Terminee)]
    [InlineData(false, StatutSaga.Annulee)]
    public async Task Un_gros_virement_attend_la_decision_d_un_analyste(bool approuve, StatutSaga attendu)
    {
        var moteur = NouveauMoteur();
        var id = moteur.Demarrer(Alice, Bob, 12_000m).Id;

        var enAttente = await Executer(moteur, id);
        Assert.Equal(StatutSaga.EnAttente, enAttente.Statut);
        Assert.Equal(38_000m, Disponible(Alice));  // les fonds sont réservés pendant l'attente

        await moteur.Signaler(id, approuve, "dossier vérifié");
        var saga = await Executer(moteur, id);

        Assert.Equal(attendu, saga.Statut);
        Assert.Equal(approuve ? 12_500m : 500m, Solde(Bob));
        Assert.Equal(approuve ? 38_000m : 50_000m, Disponible(Alice));
    }

    [Fact]
    public async Task Sans_decision_avant_l_echeance_la_saga_est_compensee()
    {
        var moteur = NouveauMoteur();
        var id = moteur.Demarrer(Alice, Bob, 12_000m).Id;
        await Executer(moteur, id);

        _horloge.Avancer(TimeSpan.FromSeconds(31));
        await moteur.Traiter();  // le moteur constate que l'échéance est dépassée
        var saga = await Executer(moteur, id);

        Assert.Equal(StatutSaga.Annulee, saga.Statut);
        Assert.Contains("échéance", saga.Raison);
        Assert.Equal(50_000m, Disponible(Alice));
    }

    [Fact]
    public async Task Apres_le_point_de_non_retour_on_ne_compense_plus()
    {
        _chaos.NotificationsHorsService = true;
        var moteur = NouveauMoteur();

        var saga = await Executer(moteur, moteur.Demarrer(Alice, Bob, 100m).Id);

        Assert.Equal(StatutSaga.ATraiterManuellement, saga.Statut);
        Assert.Equal((49_900m, 600m), (Solde(Alice), Solde(Bob)));  // l'argent est bien passé : on ne défait pas
    }

    [Fact]
    public async Task Un_nouveau_moteur_reprend_les_sagas_la_ou_l_ancien_les_avait_laissees()
    {
        var premier = NouveauMoteur();
        var id = premier.Demarrer(Alice, Carla, 100m).Id;
        await premier.Traiter();
        await premier.Traiter();  // ReserverFonds et ControlerConformite faites… puis « plantage »

        var second = NouveauMoteur();  // aucun état partagé avec le premier, sauf le magasin
        Assert.Equal(["ReserverFonds", "ControlerConformite"], _magasin.Charger(id)!.EtapesFaites);
        var saga = await Executer(second, id);

        Assert.Equal(StatutSaga.Terminee, saga.Statut);
        Assert.Equal(1_108.32m, Solde(Carla));
    }

    [Fact]
    public async Task Rejouer_une_etape_deja_faite_chez_un_participant_est_sans_effet()
    {
        // Plantage entre l'appel au participant et l'enregistrement de l'état : l'étape sera rejouée.
        var saga = Guid.NewGuid();
        await _comptes.Reserver(saga, Alice, 100m);
        await _comptes.Reserver(saga, Alice, 100m);
        await _comptes.Crediter(saga, Bob, 100m);
        await _comptes.Crediter(saga, Bob, 100m);

        Assert.Equal((49_900m, 600m), (Disponible(Alice), Solde(Bob)));
    }
}
