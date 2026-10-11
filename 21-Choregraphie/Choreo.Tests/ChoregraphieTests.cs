using System.Reflection;
using Choreo.Bus;
using Choreo.Services;
using Choreo.Services.Contrats;
using Choreo.Services.Suivi;

namespace Choreo.Tests;

public sealed class HorlogeManuelle : TimeProvider
{
    private DateTimeOffset _maintenant = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _maintenant;
    public void Avancer(TimeSpan duree) => _maintenant += duree;
}

/// <summary>Les services et le bus, sans HTTP. L'horloge est manuelle : l'expiration des réservations se teste sans attendre.</summary>
public class ChoregraphieTests : IAsyncLifetime
{
    private static readonly Guid Alice = DonneesDeDemo.Alice, Bob = DonneesDeDemo.Bob, Carla = DonneesDeDemo.Carla;

    private readonly HorlogeManuelle _horloge = new();
    private Banque _banque = null!;

    public Task InitializeAsync()
    {
        _banque = Banque.Demarrer(new OptionsBanque(), _horloge);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _banque.DisposeAsync();

    private async Task<EtatVirement> Virer(Guid source, Guid cible, decimal montant, Banque? banque = null)
    {
        banque ??= _banque;
        var id = banque.Demander(source, cible, montant);
        await banque.Bus.AttendreCalme();
        return banque.Suivi.Etat(id)!;
    }

    private decimal Solde(Guid compte) => _banque.Comptes.Consulter(compte)!.Solde;
    private decimal Reserve(Guid compte) => _banque.Comptes.Consulter(compte)!.Reserve;
    private IEnumerable<string> Types(string virement) => _banque.Bus.Journal(virement).Select(e => e.Type);

    [Fact]
    public async Task Un_virement_avance_par_reactions_en_chaine_sans_que_personne_le_conduise()
    {
        var virement = await Virer(Alice, Carla, 100m);

        Assert.Equal(StatutVirement.Regle, virement.Statut);
        var evenements = _banque.Bus.Journal(virement.Id);
        Assert.Equal(["VirementDemande", "FondsReserves", "ConformiteApprouvee", "DevisesConverties", "VirementRegle", "ClientNotifie"],
            evenements.Select(e => e.Type));
        Assert.Equal(["API", "Comptes", "Conformité", "Change", "Comptes", "Notifications"], evenements.Select(e => e.Emetteur));
        // Chacun réagit au précédent : la chaîne des causes est le seul « plan » du processus.
        Assert.All(evenements.Skip(1).Zip(evenements), paire => Assert.Equal(paire.Second.Id, paire.First.Cause));
        Assert.Equal((49_900m, 1_108.32m), (Solde(Alice), Solde(Carla)));
        Assert.True(virement.ClientNotifie);
    }

    [Fact]
    public async Task Un_refus_de_la_conformite_est_compense_par_le_service_comptes()
    {
        var virement = await Virer(Alice, DonneesDeDemo.Mallory, 50m);

        Assert.Equal((StatutVirement.Annule, "bénéficiaire sur liste noire"), (virement.Statut, virement.Motif));
        Assert.Equal(["VirementDemande", "FondsReserves", "ConformiteRefusee", "FondsLiberes"], Types(virement.Id));
        Assert.Equal((50_000m, 0m), (Solde(Alice), Reserve(Alice)));
    }

    [Fact]
    public async Task Un_echec_du_change_est_compense_lui_aussi()
    {
        var virement = await Virer(Alice, DonneesDeDemo.Yuki, 100m);

        Assert.Equal(StatutVirement.Annule, virement.Statut);
        Assert.Equal(["VirementDemande", "FondsReserves", "ConformiteApprouvee", "ChangeEchoue", "FondsLiberes"], Types(virement.Id));
        Assert.Equal(0m, Reserve(Alice));
    }

    [Fact]
    public async Task Un_solde_insuffisant_arrete_tout_des_la_premiere_reaction()
    {
        var virement = await Virer(Bob, Alice, 900m);

        Assert.Equal(StatutVirement.Refuse, virement.Statut);
        Assert.Equal(["VirementDemande", "FondsRefuses"], Types(virement.Id));
    }

    [Fact]
    public async Task Un_gros_virement_attend_la_decision_d_un_analyste()
    {
        var approuve = await Virer(Alice, Bob, 15_000m);
        var refuse = await Virer(Alice, Bob, 12_000m);
        Assert.Equal(StatutVirement.EnAttenteAnalyste, approuve.Statut);
        Assert.Equal(27_000m, Reserve(Alice));

        Assert.True(_banque.Conformite.Decider(approuve.Id, approuve: true));
        Assert.True(_banque.Conformite.Decider(refuse.Id, approuve: false));
        Assert.False(_banque.Conformite.Decider(refuse.Id, approuve: true));  // déjà décidé
        await _banque.Bus.AttendreCalme();

        Assert.Equal(StatutVirement.Regle, _banque.Suivi.Etat(approuve.Id)!.Statut);
        Assert.Equal(StatutVirement.Annule, _banque.Suivi.Etat(refuse.Id)!.Statut);
        Assert.Equal((35_000m, 0m, 15_500m), (Solde(Alice), Reserve(Alice), Solde(Bob)));
    }

    [Fact]
    public async Task L_argent_reserve_n_est_plus_disponible_pour_un_autre_virement()
    {
        await Virer(Alice, Bob, 40_000m);  // en attente d'un analyste : 40 000 € réservés, pas encore débités

        var second = await Virer(Alice, Bob, 15_000m);

        Assert.Equal(StatutVirement.Refuse, second.Statut);
        Assert.StartsWith("solde disponible insuffisant", second.Motif);
        Assert.Equal(50_000m, Solde(Alice));
    }

    [Fact]
    public async Task Un_service_en_panne_bloque_le_processus_jusqu_a_l_expiration_de_la_reservation()
    {
        _banque.Change.EnPanne = true;

        var virement = await Virer(Alice, Bob, 200m);

        // Personne n'attend le change : l'événement finit aux lettres mortes, le virement reste en plan, l'argent réservé.
        Assert.Equal("Change", _banque.Bus.LettresMortes().Single().Abonne);
        Assert.Equal(StatutVirement.EnCours, virement.Statut);
        Assert.Equal(200m, Reserve(Alice));
        _horloge.Avancer(TimeSpan.FromSeconds(20));
        Assert.Equal([virement.Id], _banque.Suivi.Bloques(_horloge.GetUtcNow(), TimeSpan.FromSeconds(10)).Select(v => v.Id));

        _horloge.Avancer(TimeSpan.FromSeconds(11));  // 31 s : la réservation (30 s) expire
        Assert.Equal(1, _banque.Comptes.ExpirerReservations());
        await _banque.Bus.AttendreCalme();

        Assert.Equal(StatutVirement.Abandonne, _banque.Suivi.Etat(virement.Id)!.Statut);
        Assert.Equal((50_000m, 0m), (Solde(Alice), Reserve(Alice)));
    }

    [Fact]
    public async Task Apres_l_expiration_ni_l_analyste_ni_un_message_en_retard_ne_reglent_le_virement()
    {
        var virement = await Virer(Alice, Bob, 15_000m);
        _horloge.Avancer(TimeSpan.FromSeconds(31));
        _banque.Comptes.ExpirerReservations();
        await _banque.Bus.AttendreCalme();

        Assert.False(_banque.Conformite.Decider(virement.Id, approuve: true));  // la conformité a écouté l'expiration

        // Une approbation tardive (le change étant en panne, rien ne suit) : le Suivi ne rouvre pas un virement terminé.
        var reserve = (FondsReserves)_banque.Bus.Journal(virement.Id)[1];
        _banque.Change.EnPanne = true;
        _banque.Bus.Publier(reserve.Suite(new ConformiteApprouvee(reserve.Ordre), "Conformité"));
        await _banque.Bus.AttendreCalme();
        Assert.Equal(StatutVirement.Abandonne, _banque.Suivi.Etat(virement.Id)!.Statut);

        // Un message très en retard (un change lent) arrive quand même : Comptes refuse de régler.
        _banque.Bus.Publier(reserve.Suite(new DevisesConverties(reserve.Ordre with { MontantCible = 15_000m }), "Change"));
        await _banque.Bus.AttendreCalme();

        Assert.Equal("VirementAbandonne", Types(virement.Id).Last());
        Assert.Equal(StatutVirement.Abandonne, _banque.Suivi.Etat(virement.Id)!.Statut);
        Assert.Equal((50_000m, 500m), (Solde(Alice), Solde(Bob)));
    }

    [Fact]
    public async Task Des_evenements_livres_deux_fois_ne_produisent_leur_effet_qu_une_fois()
    {
        await using var banque = Banque.Demarrer(new OptionsBanque { Bus = new OptionsBus { ProbabiliteDoublon = 1.0 } }, _horloge);

        for (var i = 0; i < 20; i++)
            await Virer(Alice, Bob, 10m, banque);

        Assert.Equal(49_800m, banque.Comptes.Consulter(Alice)!.Solde);
        Assert.Equal(700m, banque.Comptes.Consulter(Bob)!.Solde);
        Assert.Equal(20, banque.Bus.Journal().Count(e => e is VirementRegle));  // chaque service n'a réagi qu'une fois
        Assert.Equal(20, banque.Notifications.Envoyees().Count);
        Assert.Equal(20, banque.Fidelite!.Points(Alice));
    }

    [Fact]
    public async Task Apres_le_point_de_non_retour_une_notification_en_echec_est_reessayee_jamais_compensee()
    {
        _banque.Notifications.Saboter(3);
        var reessaye = await Virer(Alice, Bob, 10m);
        _banque.Notifications.Saboter(10);
        var abandonne = await Virer(Alice, Bob, 10m);

        Assert.True(reessaye.ClientNotifie);  // 3 échecs, puis la 4e tentative passe
        Assert.Equal(StatutVirement.Regle, abandonne.Statut);  // l'argent est parti : on ne défait rien
        Assert.False(abandonne.ClientNotifie);
        Assert.Equal(("Notifications", "VirementRegle"), (_banque.Bus.LettresMortes().Single().Abonne, _banque.Bus.LettresMortes().Single().Evenement.Type));
    }

    [Fact]
    public async Task Ajouter_un_abonne_ne_change_rien_pour_les_autres()
    {
        await using var sans = Banque.Demarrer(new OptionsBanque { Fidelite = false }, _horloge);

        var avec = await Virer(Alice, Carla, 100m);
        var sansFidelite = await Virer(Alice, Carla, 100m, sans);

        Assert.Equal(Types(avec.Id), sans.Bus.Journal(sansFidelite.Id).Select(e => e.Type));
        Assert.Equal(10, _banque.Fidelite!.Points(Alice));
        Assert.DoesNotContain("Fidélité", sans.Bus.Abonnes().Select(a => a.Abonne));
    }

    [Fact]
    public void Aucun_service_ne_connait_les_autres()
    {
        // Test d'architecture : un service ne manipule que ses propres types, les contrats, le bus et les outils communs.
        string[] services = ["Comptes", "Conformite", "Change", "Notifications", "Fidelite", "Suivi"];
        var assemblage = typeof(Banque).Assembly;
        static string? Service(Type t) => t.Namespace?.StartsWith("Choreo.Services.") == true ? t.Namespace.Split('.')[2] : null;

        foreach (var type in assemblage.GetTypes().Where(t => services.Contains(Service(t))))
        {
            const BindingFlags tout = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var utilises = type.GetFields(tout).Select(f => f.FieldType)
                .Concat(type.GetProperties(tout).Select(p => p.PropertyType))
                .Concat(type.GetConstructors(tout).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                .Concat(type.GetMethods(tout).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
                .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : [t]);

            foreach (var utilise in utilises)
                Assert.True(Service(utilise) is null || Service(utilise) == "Contrats" || Service(utilise) == Service(type),
                    $"{type.FullName} utilise {utilise.FullName}");
        }
    }
}
