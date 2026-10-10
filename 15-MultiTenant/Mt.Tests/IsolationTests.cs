using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Mt.Banque;
using Mt.Locataires;

namespace Mt.Tests;

/// <summary>Le garde-fou des données : filtre global, estampillage, refus des écritures croisées, choix de la base.</summary>
public class IsolationTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FabriqueBanque _fabrique = new(new InMemoryDatabaseRoot());

    private static Locataire Nouveau(string id, Plan plan = Plan.Standard, ReglesLocataire? regles = null) =>
        Formules.Creer(new DefinitionLocataire(id, $"Banque {id}", plan, regles ?? new ReglesLocataire(1_000m, 0m, 0m, 10)));

    private ServiceBancaire Banque(Locataire locataire) =>
        new(_fabrique.Ouvrir(locataire), ContexteLocataire.Pour(locataire), TimeProvider.System);

    private async Task Semer(Locataire locataire, params Compte[] comptes)
    {
        await using var db = _fabrique.Ouvrir(locataire);
        db.Comptes.AddRange(comptes);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Le_meme_identifiant_designe_un_compte_different_chez_chaque_locataire()
    {
        var alpha = Nouveau("alpha");
        var beta = Nouveau("beta");
        await Semer(alpha, new Compte(Alice, "Alice", 1_000m));
        await Semer(beta, new Compte(Alice, "Alice", 3_000m));  // même Guid, même base partagée : la clé inclut le locataire

        Assert.Equal(1_000m, (await Banque(alpha).Consulter(Alice))!.Solde);
        Assert.Equal(3_000m, (await Banque(beta).Consulter(Alice))!.Solde);
    }

    [Fact]
    public async Task Un_locataire_ne_voit_ni_ne_touche_les_comptes_d_un_autre()
    {
        var alpha = Nouveau("alpha");
        var beta = Nouveau("beta");
        var compte = await Banque(alpha).Ouvrir("Zoé", 100m);

        Assert.Null(await Banque(beta).Consulter(compte.Id));
        Assert.DoesNotContain(await Banque(beta).Lister(), c => c.Id == compte.Id);
        await Assert.ThrowsAsync<CompteIntrouvableException>(() => Banque(beta).Retirer(compte.Id, 10m));
        Assert.Equal(100m, (await Banque(alpha).Consulter(compte.Id))!.Solde);
    }

    [Fact]
    public async Task Sans_locataire_une_requete_echoue_au_lieu_de_tout_renvoyer()
    {
        await Semer(Nouveau("alpha"), new Compte(Alice, "Alice", 1_000m));
        await using var db = _fabrique.OuvrirSansLocataire(FabriqueBanque.BasePartagee);

        await Assert.ThrowsAsync<AucunLocataireException>(() => db.Comptes.ToListAsync());
        // Voir tous les locataires reste possible, mais seulement de façon explicite.
        Assert.Single(await db.Comptes.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Une_insertion_est_estampillee_avec_le_locataire_courant()
    {
        var compte = await Banque(Nouveau("alpha")).Ouvrir("Zoé", 100m);

        await using var db = _fabrique.OuvrirSansLocataire(FabriqueBanque.BasePartagee);
        Assert.Equal("alpha", (await db.Comptes.IgnoreQueryFilters().SingleAsync(c => c.Id == compte.Id)).LocataireId);
        Assert.All(await db.Operations.IgnoreQueryFilters().ToListAsync(), o => Assert.Equal("alpha", o.LocataireId));
    }

    [Fact]
    public async Task Enregistrer_une_donnee_d_un_autre_locataire_est_refuse()
    {
        var alpha = Nouveau("alpha");
        await Semer(alpha, new Compte(Alice, "Alice", 1_000m));
        Compte compteAlpha;
        await using (var db = _fabrique.Ouvrir(alpha))
            compteAlpha = await db.Comptes.AsNoTracking().SingleAsync();

        // Un bogue fait passer une entité d'alpha au DbContext de beta (cache partagé, variable statique…).
        await using var dbBeta = _fabrique.Ouvrir(Nouveau("beta"));
        compteAlpha.Debiter(1_000m);
        dbBeta.Update(compteAlpha);

        await Assert.ThrowsAsync<FuiteEntreLocatairesException>(() => dbBeta.SaveChangesAsync());
    }

    [Fact]
    public async Task Les_donnees_d_un_locataire_premium_sont_dans_sa_propre_base()
    {
        var gamma = Nouveau("gamma", Plan.Premium);
        await Banque(gamma).Ouvrir("Zoé", 100m);
        await Banque(Nouveau("alpha")).Ouvrir("Yann", 50m);

        Assert.Equal(["banque-gamma", "banque-partagee"], _fabrique.Bases);
        await using var partagee = _fabrique.OuvrirSansLocataire(FabriqueBanque.BasePartagee);
        Assert.DoesNotContain(await partagee.Comptes.IgnoreQueryFilters().ToListAsync(), c => c.LocataireId == "gamma");
    }

    [Fact]
    public void Toute_donnee_d_un_locataire_est_filtree_et_porte_le_locataire_dans_sa_cle()
    {
        // Test d'architecture : une entité ajoutée demain sera protégée, ou ce test échouera.
        var typesDuLocataire = typeof(Compte).Assembly.GetTypes()
            .Where(t => t.IsClass && typeof(IAppartientAUnLocataire).IsAssignableFrom(t)).ToList();
        using var db = _fabrique.OuvrirSansLocataire("modele");

        Assert.NotEmpty(typesDuLocataire);
        foreach (var type in typesDuLocataire)
        {
            var entite = db.Model.FindEntityType(type);
            Assert.True(entite is not null, $"{type.Name} n'est pas dans le modèle EF.");
            Assert.NotEmpty(entite.GetDeclaredQueryFilters());
            Assert.Contains(entite.FindPrimaryKey()!.Properties, p => p.Name == nameof(IAppartientAUnLocataire.LocataireId));
        }
    }

    [Fact]
    public void Le_contexte_ne_change_pas_de_locataire_en_cours_de_route()
    {
        var contexte = ContexteLocataire.Pour(Nouveau("alpha"));

        Assert.Throws<InvalidOperationException>(() => contexte.Definir(Nouveau("beta")));
        Assert.Throws<AucunLocataireException>(() => new ContexteLocataire().Exiger());
    }

    [Fact]
    public async Task Les_regles_metier_sont_celles_du_locataire()
    {
        var bob = Guid.NewGuid();
        var prudente = Nouveau("prudente", regles: new ReglesLocataire(PlafondVirement: 500m, FraisVirement: 0m, FraisMensuels: 0m, ComptesMax: 10));
        var payante = Nouveau("payante", regles: new ReglesLocataire(PlafondVirement: 5_000m, FraisVirement: 0.5m, FraisMensuels: 0m, ComptesMax: 10));
        await Semer(prudente, new Compte(Alice, "Alice", 1_000m), new Compte(bob, "Bob", 0m));
        await Semer(payante, new Compte(Alice, "Alice", 1_000m), new Compte(bob, "Bob", 0m));

        var refus = await Assert.ThrowsAsync<RegleMetierException>(() => Banque(prudente).Virer(Alice, bob, 800m));
        var virement = await Banque(payante).Virer(Alice, bob, 800m);

        Assert.Contains("Plafond", refus.Message);
        Assert.Equal(0.5m, virement.Frais);
        Assert.Equal(199.5m, virement.SoldeSource);
    }

    [Fact]
    public async Task Le_quota_de_comptes_est_celui_du_locataire()
    {
        var petite = Nouveau("petite", regles: new ReglesLocataire(1_000m, 0m, 0m, ComptesMax: 2));
        await Banque(petite).Ouvrir("Un", 0m);
        await Banque(petite).Ouvrir("Deux", 0m);
        await Banque(Nouveau("voisine")).Ouvrir("Trois", 0m);  // les comptes d'une autre banque ne comptent pas

        var refus = await Assert.ThrowsAsync<RegleMetierException>(() => Banque(petite).Ouvrir("Trois", 0m));
        Assert.Contains("quota de 2 comptes", refus.Message);
    }

    [Fact]
    public async Task Les_frais_mensuels_ne_sont_preleves_qu_une_fois_par_mois()
    {
        var alpha = Nouveau("alpha", regles: new ReglesLocataire(1_000m, 0m, FraisMensuels: 2m, ComptesMax: 10));
        await Semer(alpha, new Compte(Alice, "Alice", 1_000m));

        Assert.Equal(1, await Banque(alpha).PreleverFraisMensuels("2026-10"));
        Assert.Equal(0, await Banque(alpha).PreleverFraisMensuels("2026-10"));
        Assert.Equal(1, await Banque(alpha).PreleverFraisMensuels("2026-11"));
        Assert.Equal(996m, (await Banque(alpha).Consulter(Alice))!.Solde);
    }
}
