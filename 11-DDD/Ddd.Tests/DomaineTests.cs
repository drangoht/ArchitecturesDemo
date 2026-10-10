using Ddd.Conformite;
using Ddd.TenueDeCompte.Domaine;
using Ddd.TenueDeCompte.Domaine.Comptes;
using Ddd.TenueDeCompte.Domaine.ObjetsValeurs;
using Ddd.TenueDeCompte.Infrastructure;

namespace Ddd.Tests;

public class DomaineTests
{
    private static readonly DateTime Lundi = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Argent Euros(decimal montant) => new(montant, Devise.Euro);

    private static async Task<Compte> NouveauCompte(string titulaire, Argent depot)
    {
        var fabrique = new FabriqueDeComptes(new ComptesEnMemoire());
        var compte = await fabrique.Ouvrir(NomTitulaire.Depuis(titulaire), depot, Lundi);
        compte.ExtraireEvenements();
        return compte;
    }

    [Fact]
    public async Task Le_plafond_de_retrait_porte_sur_la_journee()
    {
        var compte = await NouveauCompte("Alice", Euros(5_000m));
        compte.Retirer(Euros(600m), Lundi);

        var erreur = Assert.Throws<RegleMetierException>(() => compte.Retirer(Euros(600m), Lundi.AddHours(3)));
        compte.Retirer(Euros(600m), Lundi.AddDays(1));  // le lendemain, c'est permis

        Assert.Contains("Plafond de retrait journalier", erreur.Message);
        Assert.Equal(Euros(3_800m), compte.Solde);
    }

    [Fact]
    public async Task Un_compte_bloque_ne_peut_plus_etre_debite()
    {
        var compte = await NouveauCompte("Alice", Euros(500m));
        compte.Bloquer("Contrôle", Lundi);

        Assert.Throws<RegleMetierException>(() => compte.Retirer(Euros(10m), Lundi));
        Assert.IsType<CompteBloque>(Assert.Single(compte.EvenementsDomaine));
    }

    [Fact]
    public async Task Un_virement_entre_devises_passe_par_la_couche_anti_corruption()
    {
        var alice = await NouveauCompte("Alice", Euros(500m));
        var carla = await NouveauCompte("Carla", new Argent(0m + 100m, Devise.DollarUs));
        var service = new ServiceDeVirement(new AdaptateurTauxDeChange(new SystemeDeCotationsHerite()));

        await service.Virer(alice, carla, Euros(100m), Lundi);

        Assert.Equal(Euros(400m), alice.Solde);
        Assert.Equal(new Argent(208.32m, Devise.DollarUs), carla.Solde);  // 100 € × 1,0832
        var virement = Assert.IsType<VirementEffectue>(Assert.Single(alice.EvenementsDomaine));
        Assert.Equal(new Argent(108.32m, Devise.DollarUs), virement.MontantCredite);
    }

    [Fact]
    public async Task La_couche_anti_corruption_calcule_les_taux_croises_et_ignore_les_devises_inconnues()
    {
        var adaptateur = new AdaptateurTauxDeChange(new SystemeDeCotationsHerite());

        var francs = await adaptateur.Convertir(new Argent(108.32m, Devise.DollarUs), Devise.FrancSuisse);

        Assert.Equal(new Argent(94.15m, Devise.FrancSuisse), francs);  // USD → EUR → CHF ; GBP et JPY ignorés
    }

    [Fact]
    public void La_conformite_signale_un_gros_virement_puis_un_fractionnement()
    {
        var dossier = new DossierDeVigilance(Guid.NewGuid(), "Bob");

        var gros = dossier.Observer(new VirementObserve(Lundi, 12_000m, "FR76…"));
        dossier.Observer(new VirementObserve(Lundi.AddMinutes(2), 50m, "FR76…"));
        var troisieme = dossier.Observer(new VirementObserve(Lundi.AddMinutes(4), 50m, "FR76…"));

        Assert.Equal(NiveauAlerte.Elevee, Assert.Single(gros).Niveau);
        Assert.Equal(NiveauAlerte.Critique, Assert.Single(troisieme).Niveau);
        Assert.True(dossier.BlocageDemande);
    }

    private class ComptesEnMemoire : IComptes
    {
        public Task<Compte?> ParId(CompteId id) => Task.FromResult<Compte?>(null);
        public Task<Compte?> ParIban(Iban iban) => Task.FromResult<Compte?>(null);
        public Task<IReadOnlyList<Compte>> Tous() => Task.FromResult<IReadOnlyList<Compte>>([]);
        public Task Ajouter(Compte compte) => Task.CompletedTask;
        public Task<long> ProchainNumeroDeCompte() => Task.FromResult(42L);
    }
}
