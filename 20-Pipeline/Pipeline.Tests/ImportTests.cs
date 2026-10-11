using Pipeline.Banque;
using Pipeline.Noyau;

namespace Pipeline.Tests;

/// <summary>Les filtres bancaires, assemblés comme dans l'API, sur le fichier d'exemple et sur un gros fichier généré.</summary>
public class ImportTests
{
    private const string Alice = DonneesDeDemo.Alice, Bob = DonneesDeDemo.Bob;

    private readonly Registre _registre = DonneesDeDemo.Registre();

    /// <summary>Le fichier d'exemple de l'API (Virements-exemple.csv), retrouvé depuis le dossier de sortie des tests.</summary>
    private static string[] FichierExemple()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dossier.FullName, "Pipeline.Api", "Virements-exemple.csv")))
            dossier = dossier.Parent!;
        return File.ReadAllLines(Path.Combine(dossier.FullName, "Pipeline.Api", "Virements-exemple.csv"));
    }

    private static async IAsyncEnumerable<string> Source(IEnumerable<string> lignes)
    {
        using var lecteur = new StringReader(string.Join('\n', lignes));
        await foreach (var ligne in Imports.Lignes(lecteur))
            yield return ligne;
    }

    private Task<Bilan> Importer(OptionsImport? options = null) =>
        Imports.Executer(_registre, options ?? new OptionsImport(ParallelismeFraude: 4)).Executer(Source(FichierExemple()));

    private decimal Solde(string iban) => _registre.Consulter(iban)!.Solde;

    [Fact]
    public async Task Le_fichier_d_exemple_donne_trois_virements_et_un_rejet_par_motif()
    {
        var bilan = await Importer();

        Assert.Equal((StatutExecution.Termine, 11L, 3L, 8L), (bilan.Statut, bilan.Lignes, bilan.Sorties, bilan.Rejets));
        Assert.All(bilan.RejetsParMotif.Values, n => Assert.Equal(1, n));
        Assert.Equal(
            [(3L, "Analyser"), (4, "Valider"), (5, "Valider"), (6, "Dédupliquer"), (7, "Convertir"), (8, "Contrôler la fraude"),
             (9, "Contrôler la fraude"), (10, "Exécuter")],
            bilan.PremiersRejets.Select(r => (r.Ligne, r.Filtre)));
        Assert.Equal((842.80m, 657.20m), (Solde(Alice), Solde(Bob)));  // -120 +46 (50 USD) -83,20 (80 CHF)
    }

    [Fact]
    public async Task Rejouer_un_fichier_n_execute_rien_deux_fois()
    {
        await Importer();

        var rejeu = await Importer();

        Assert.Equal(4, rejeu.RejetsParMotif["doublon"]);  // VIR-001, 002, 010 déjà exécutés, et VIR-001 en double dans le fichier
        // Écartés tôt, par un filtre bon marché : ils n'atteignent ni le contrôle de fraude, ni l'exécution.
        Assert.All(rejeu.PremiersRejets.Where(r => r.Motif == "doublon"), r => Assert.Equal("Dédupliquer", r.Filtre));
        Assert.Equal(1, rejeu.Sorties);                     // seul VIR-009 passe : Bob a maintenant de quoi payer
        Assert.Equal((1_442.80m, 57.20m), (Solde(Alice), Solde(Bob)));
    }

    [Fact]
    public async Task Un_import_a_blanc_dit_ce_qui_passerait_sans_rien_modifier()
    {
        var sorties = new List<Virement>();

        var bilan = await Imports.ABlanc(_registre, new OptionsImport()).Executer(Source(FichierExemple()), sorties.Add);

        Assert.Equal(["VIR-001", "VIR-002", "VIR-009", "VIR-010"], sorties.Select(v => v.Reference));
        Assert.DoesNotContain("Exécuter", bilan.Etages.Select(e => e.Nom));
        Assert.Equal((1_000m, 500m, 0), (Solde(Alice), Solde(Bob), _registre.NombreExecutes));
    }

    [Fact]
    public async Task Apres_une_panne_en_plein_import_le_rejeu_termine_sans_rien_executer_deux_fois()
    {
        var panne = await Importer(new OptionsImport(PanneALaReference: "VIR-010"));

        Assert.Equal(StatutExecution.Echoue, panne.Statut);
        Assert.Equal("Panne simulée en traitant VIR-010.", panne.Erreur);
        Assert.InRange(_registre.NombreExecutes, 0, 2);  // selon ce qui avait atteint l'exécution avant l'arrêt

        var reprise = await Importer();

        Assert.Equal(StatutExecution.Termine, reprise.Statut);
        Assert.Equal(3, _registre.NombreExecutes);
        Assert.Equal((842.80m, 657.20m), (Solde(Alice), Solde(Bob)));  // exactement comme sans panne
    }

    [Fact]
    public async Task L_ordre_du_fichier_compte_et_l_etage_parallele_ordonne_le_respecte()
    {
        // Bob ne peut payer la ligne 2 qu'après avoir reçu la ligne 1. La ligne 1 est la plus lente à contrôler.
        string[] fichier = [$"L1;{Alice};{Bob};600;EUR", $"L2;{Bob};{Alice};1000;EUR"];
        Pipeline<string, VirementExecute> Assembler(Registre registre, bool ordonne) => Noyau.Pipeline.Depuis<string>()
            .Puis(new Analyser())
            .Puis(new Convertir())
            .Puis(new Lent<Virement>("Contrôle lent", v => v.Reference == "L1" ? 100 : 0), parallelisme: 4, ordonne)
            .Puis(new Executer(registre))
            .Construire();

        var ordonne = await Assembler(_registre, ordonne: true).Executer(Source(fichier));
        var autreRegistre = DonneesDeDemo.Registre();
        var desordonne = await Assembler(autreRegistre, ordonne: false).Executer(Source(fichier));

        Assert.Equal(2, ordonne.Sorties);
        Assert.Equal(1, desordonne.Sorties);
        Assert.Equal("solde insuffisant", desordonne.PremiersRejets.Single().Motif);
    }

    [Fact]
    public async Task Un_gros_fichier_passe_en_flux_avec_une_memoire_bornee()
    {
        var (registre, comptes) = Imports.BacASable(200);
        var total = registre.Soldes().Values.Sum();

        var bilan = await Imports.Executer(registre, new OptionsImport(ParallelismeFraude: 4)).Executer(Imports.FichierDeDemo(20_000, comptes));

        Assert.Equal((StatutExecution.Termine, 20_000L), (bilan.Statut, bilan.Lignes));
        Assert.Equal(bilan.Lignes, bilan.Sorties + bilan.Rejets);
        foreach (var motif in new[] { "ligne mal formée", "IBAN invalide", "doublon", "plafond dépassé" })
            Assert.Equal(200, bilan.RejetsParMotif[motif]);
        Assert.InRange(bilan.MaxEnTransit, 1, 1_000);  // 20 000 lignes, jamais plus de quelques centaines en mémoire
        Assert.Equal(total, registre.Soldes().Values.Sum());
    }

    [Fact]
    public async Task Paralleliser_le_controle_de_fraude_ne_change_pas_le_resultat()
    {
        var (seul, comptes) = Imports.BacASable(50);
        var (huit, _) = Imports.BacASable(50);

        await Imports.Executer(seul, new OptionsImport(ParallelismeFraude: 1, CoutScoring: 1)).Executer(Imports.FichierDeDemo(5_000, comptes));
        await Imports.Executer(huit, new OptionsImport(ParallelismeFraude: 8, CoutScoring: 1)).Executer(Imports.FichierDeDemo(5_000, comptes));

        Assert.Equal(seul.Soldes(), huit.Soldes());
    }

    [Fact]
    public async Task Executer_refuse_un_virement_qui_n_est_pas_passe_par_la_conversion()
    {
        // Les filtres s'ignorent, mais le pipeline a un ordre logique : Exécuter suppose que Convertir est passé avant.
        var bilan = await Noyau.Pipeline.Depuis<string>().Puis(new Analyser()).Puis(new Executer(_registre)).Construire()
            .Executer(Source([$"X-1;{Alice};{Bob};100;EUR"]));

        Assert.Equal("montant non converti", bilan.PremiersRejets.Single().Motif);
        Assert.Equal(1_000m, Solde(Alice));
    }

    [Fact]
    public void Le_registre_n_execute_jamais_deux_fois_la_meme_reference()
    {
        // Dernier rempart : deux imports SIMULTANÉS du même fichier passent tous deux Dédupliquer avant que l'un ait exécuté.
        var virement = new Virement("R-1", Alice, Bob, 100m, "EUR", 100m);

        Assert.Null(_registre.Virer(virement, out _));
        Assert.Equal("doublon", _registre.Virer(virement, out _));
        Assert.Equal(900m, Solde(Alice));
    }

    [Theory]
    [InlineData("FR7630006000011234567890189", true)]
    [InlineData("FR7630006000011234567890188", false)]
    [InlineData("FR76 3000", false)]
    public void La_cle_d_un_iban_est_verifiee(string iban, bool valide) => Assert.Equal(valide, Iban.EstValide(iban));
}
