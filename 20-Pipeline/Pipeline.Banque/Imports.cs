using System.Globalization;
using System.Runtime.CompilerServices;
using Pipeline.Noyau;

namespace Pipeline.Banque;

public sealed record OptionsImport(int ParallelismeFraude = 1, bool Ordonne = true, int CoutScoring = 0, string? PanneALaReference = null);

/// <summary>
/// L'assemblage : les mêmes filtres donnent deux pipelines. L'import exécute les virements ; l'import « à blanc »
/// s'arrête avant l'exécution et dit seulement ce qui serait rejeté.
/// </summary>
public static class Imports
{
    /// <summary>Les contrôles, communs aux deux pipelines. Les filtres bon marché d'abord : ce qu'ils rejettent n'atteint pas les coûteux.</summary>
    private static Constructeur<string, Virement> Controles(Registre registre, OptionsImport options) =>
        Noyau.Pipeline.Depuis<string>()
            .Puis(new Analyser())
            .Puis(new Valider())
            .Puis(new Dedupliquer(registre))
            .Puis(new Convertir())
            .Puis(new ControlerFraude(options.CoutScoring), options.ParallelismeFraude, options.Ordonne);

    /// <summary>Un pipeline NEUF par import : Dédupliquer a un état propre à chaque fichier.</summary>
    public static Pipeline<string, VirementExecute> Executer(Registre registre, OptionsImport options)
    {
        var controles = Controles(registre, options);
        if (options.PanneALaReference is { } reference)
            controles = controles.Puis(new PanneSimulee(reference));
        return controles.Puis(new Executer(registre)).Construire();
    }

    public static Pipeline<string, Virement> ABlanc(Registre registre, OptionsImport options) => Controles(registre, options).Construire();

    /// <summary>La source : les lignes d'un fichier, lues au fur et à mesure (l'en-tête et les lignes vides sont sautés).</summary>
    public static async IAsyncEnumerable<string> Lignes(TextReader lecteur, [EnumeratorCancellation] CancellationToken annulation = default)
    {
        while (await lecteur.ReadLineAsync(annulation) is { } ligne)
            if (!string.IsNullOrWhiteSpace(ligne) && !ligne.StartsWith("reference;", StringComparison.OrdinalIgnoreCase))
                yield return ligne;
    }

    /// <summary>
    /// Un gros fichier de démonstration, GÉNÉRÉ à la volée (jamais en mémoire entier), avec quelques lignes fautives de chaque sorte.
    /// </summary>
    public static async IAsyncEnumerable<string> FichierDeDemo(int lignes, IReadOnlyList<string> comptes, int graine = 42)
    {
        var hasard = new Random(graine);
        for (var i = 1; i <= lignes; i++)
        {
            var indice = hasard.Next(comptes.Count);
            var emetteur = comptes[indice];
            var beneficiaire = comptes[(indice + 1 + hasard.Next(comptes.Count - 1)) % comptes.Count];  // jamais lui-même
            var montant = (hasard.Next(100, 20_000) / 100m).ToString(CultureInfo.InvariantCulture);
            var ligne = (i % 100) switch
            {
                17 => $"MASSIF-{i};{emetteur};{beneficiaire}",                                    // mal formée
                33 => $"MASSIF-{i};FR0000000000000000000000000;{beneficiaire};{montant};EUR",  // IBAN invalide
                51 => $"MASSIF-{i - 1};{emetteur};{beneficiaire};{montant};EUR",               // doublon
                67 => $"MASSIF-{i};{emetteur};{beneficiaire};{montant};JPY",                   // devise inconnue
                89 => $"MASSIF-{i};{emetteur};{beneficiaire};25000;EUR",                       // plafond
                _ => $"MASSIF-{i};{emetteur};{beneficiaire};{montant};{(i % 7 == 0 ? "USD" : "EUR")}"
            };
            yield return ligne;
            if (i % 1_000 == 0)
                await Task.Yield();
        }
    }

    /// <summary>Une banque de démonstration : <paramref name="nombre"/> comptes bien approvisionnés.</summary>
    public static (Registre Registre, IReadOnlyList<string> Comptes) BacASable(int nombre = 1_000)
    {
        var registre = new Registre();
        var comptes = Enumerable.Range(1, nombre).Select(i => Iban.Fabriquer($"30006{i:D18}")).ToList();
        foreach (var (iban, i) in comptes.Select((c, i) => (c, i)))
            registre.Ouvrir(iban, $"Client {i + 1}", 1_000_000m);
        return (registre, comptes);
    }
}
