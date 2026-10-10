using Es.Domaine;
using Es.Stockage;

namespace Es.Tests;

public class StockageTests
{
    private static readonly DateTime T = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Deux_ecritures_basees_sur_la_meme_version_la_seconde_est_refusee()
    {
        var magasin = new MagasinEvenements();
        var id = Guid.NewGuid();
        await magasin.Ajouter(new AEcrire(id, 0, [new CompteOuvert(id, "Alice", 100m, T)]));

        // Deux utilisateurs ont chargé le compte en version 1 et décident chacun d'un retrait.
        await magasin.Ajouter(new AEcrire(id, 1, [new ArgentRetire(id, 80m, "A", T)]));
        var second = () => magasin.Ajouter(new AEcrire(id, 1, [new ArgentRetire(id, 80m, "B", T)]));

        await Assert.ThrowsAsync<ConflitDeConcurrenceException>(second);
        Assert.Equal(2, (await magasin.LireFlux(id)).Count);
    }

    [Fact]
    public async Task Une_ecriture_sur_plusieurs_flux_est_en_tout_ou_rien()
    {
        var magasin = new MagasinEvenements();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        await magasin.Ajouter(
            new AEcrire(a, 0, [new CompteOuvert(a, "A", 100m, T)]),
            new AEcrire(b, 0, [new CompteOuvert(b, "B", 100m, T)]));

        // La version attendue de b est fausse : rien n'est écrit, pas même pour a.
        var virement = () => magasin.Ajouter(
            new AEcrire(a, 1, [new VirementEmis(a, Guid.NewGuid(), b, 10m, T)]),
            new AEcrire(b, 5, [new VirementRecu(b, Guid.NewGuid(), a, 10m, T)]));

        await Assert.ThrowsAsync<ConflitDeConcurrenceException>(virement);
        Assert.Equal(2, (await magasin.LireTout()).Count);
    }

    [Fact]
    public async Task Le_fichier_suffit_a_tout_retrouver_apres_un_redemarrage()
    {
        var fichier = Path.Combine(Path.GetTempPath(), $"evenements-{Guid.NewGuid()}.jsonl");
        try
        {
            var id = Guid.NewGuid();
            var avant = new MagasinEvenements(fichier);
            await avant.Ajouter(new AEcrire(id, 0, [new CompteOuvert(id, "Alice", 100m, T), new ArgentRetire(id, 40m, "Courses", T)]));

            var apres = new MagasinEvenements(fichier);  // « redémarrage »

            var compte = Compte.Reconstituer((await apres.LireFlux(id)).Select(e => e.Evenement()));
            Assert.Equal(60m, compte.Solde);
            Assert.Equal(2, File.ReadAllLines(fichier).Length);
        }
        finally
        {
            File.Delete(fichier);
        }
    }

    [Fact]
    public void Un_ancien_format_d_evenement_est_converti_a_la_lecture()
    {
        // Retrait enregistré par une ancienne version de l'application, avant l'ajout du motif.
        const string jsonV1 = """{"compteId":"11111111-1111-1111-1111-111111111111","montant":40,"date":"2025-01-15T10:00:00Z"}""";

        var evenement = SerialiseurEvenements.Deserialiser("ArgentRetire", jsonV1);

        var retrait = Assert.IsType<ArgentRetire>(evenement);
        Assert.Equal((40m, "Non précisé"), (retrait.Montant, retrait.Motif));
    }

    [Fact]
    public async Task Un_instantane_evite_de_rejouer_tout_l_historique_sans_changer_le_resultat()
    {
        var magasin = new MagasinEspion(new MagasinEvenements());
        var depot = new DepotComptes(magasin, new MagasinInstantanes());
        var id = Guid.NewGuid();
        await depot.Enregistrer(Compte.Ouvrir(id, "Alice", 1000m, T));
        for (var i = 1; i <= 11; i++)
        {
            var compte = await depot.Charger(id);
            compte.Retirer(10m, $"Retrait {i}", T);
            await depot.Enregistrer(compte);
        }

        magasin.EvenementsLus = 0;
        var avecInstantane = await depot.Charger(id);
        var sansInstantane = Compte.Reconstituer((await magasin.LireFlux(id)).Select(e => e.Evenement()));

        Assert.Equal(sansInstantane.Instantane(), avecInstantane.Instantane());
        Assert.Equal(12, avecInstantane.Version);
        Assert.Equal(2 + 12, magasin.EvenementsLus);  // 2 lus après l'instantané v10, puis 12 pour la comparaison
    }

    private class MagasinEspion(IMagasinEvenements interne) : IMagasinEvenements
    {
        public int EvenementsLus { get; set; }

        public async Task<IReadOnlyList<EvenementStocke>> LireFlux(Guid flux, int apresVersion = 0)
        {
            var lus = await interne.LireFlux(flux, apresVersion);
            EvenementsLus += lus.Count;
            return lus;
        }

        public Task<IReadOnlyList<EvenementStocke>> LireTout(long apresPosition = 0, int maximum = int.MaxValue) =>
            interne.LireTout(apresPosition, maximum);

        public Task Ajouter(params AEcrire[] ecritures) => interne.Ajouter(ecritures);
    }
}
