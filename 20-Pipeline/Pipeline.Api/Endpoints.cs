using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using Pipeline.Banque;
using Pipeline.Noyau;

namespace Pipeline.Api;

public sealed class OptionsApi
{
    /// <summary>Copies du filtre de fraude qui travaillent en même temps, par défaut.</summary>
    public int ParallelismeFraude { get; set; } = 4;

    /// <summary>Coût simulé du score de fraude (en milliers de tours de hachage par virement).</summary>
    public int CoutScoring { get; set; } = 50;
}

public sealed record ImportEnregistre(int Id, DateTimeOffset Date, bool ABlanc, Bilan Bilan);

/// <summary>Les comptes rendus des imports déjà faits.</summary>
public sealed class Historique
{
    private readonly ConcurrentDictionary<int, ImportEnregistre> _imports = new();
    private int _dernier;

    public ImportEnregistre Ajouter(bool aBlanc, Bilan bilan)
    {
        var import = new ImportEnregistre(Interlocked.Increment(ref _dernier), DateTimeOffset.Now, aBlanc, bilan);
        _imports[import.Id] = import;
        return import;
    }

    public ImportEnregistre? Trouver(int id) => _imports.GetValueOrDefault(id);

    public IEnumerable<ImportEnregistre> Tous() => _imports.Values.OrderBy(i => i.Id);
}

internal static class Endpoints
{
    internal record ImportMassifRequete(int Lignes = 100_000, int ParallelismeFraude = 1, bool Ordonne = true, int? CoutScoring = null);

    public static void MapComptes(this IEndpointRouteBuilder app) =>
        app.MapGet("/comptes/{iban}", (string iban, Registre registre) =>
            registre.Consulter(iban.ToUpperInvariant()) is { } compte ? Results.Ok(compte) : Results.NotFound());

    public static void MapImports(this IEndpointRouteBuilder app)
    {
        // Le corps de la requête est le fichier CSV, lu ligne à ligne PENDANT qu'il arrive : il n'est jamais chargé en entier.
        app.MapPost("/imports", async (HttpRequest requete, bool? aBlanc, int? parallelisme, string? panneALaReference,
            Registre registre, Historique historique, IOptions<OptionsApi> configuration, CancellationToken annulation) =>
        {
            var options = new OptionsImport(parallelisme ?? configuration.Value.ParallelismeFraude, Ordonne: true,
                configuration.Value.CoutScoring, panneALaReference);
            using var lecteur = new StreamReader(requete.Body, Encoding.UTF8);
            var lignes = Imports.Lignes(lecteur, annulation);

            var bilan = aBlanc == true
                ? await Imports.ABlanc(registre, options).Executer(lignes, annulation: annulation)
                : await Imports.Executer(registre, options).Executer(lignes, annulation: annulation);

            var import = historique.Ajouter(aBlanc == true, bilan);
            return Results.Json(import, statusCode: bilan.Statut == StatutExecution.Termine ? 200 : 500);
        });

        app.MapGet("/imports", (Historique historique) => Results.Ok(historique.Tous().Select(i => new
        {
            i.Id, i.Date, i.ABlanc, i.Bilan.Statut, i.Bilan.Lignes, i.Bilan.Sorties, i.Bilan.Rejets
        })));

        app.MapGet("/imports/{id:int}", (int id, Historique historique) =>
            historique.Trouver(id) is { } import ? Results.Ok(import) : Results.NotFound());
    }

    public static void MapDemonstrations(this IEndpointRouteBuilder app) =>
        // Un fichier de N lignes généré à la volée, importé dans une banque de démonstration de 1 000 comptes.
        // Comparer parallelismeFraude = 1 et 8 : le goulot, le débit, et la mémoire (maxEnTransit) qui ne dépend pas de N.
        app.MapPost("/demo/import-massif", async (ImportMassifRequete r, IOptions<OptionsApi> configuration, CancellationToken annulation) =>
        {
            if (r.Lignes is < 1 or > 2_000_000 || r.ParallelismeFraude is < 1 or > 64)
                return Results.BadRequest(new { erreur = "Lignes : de 1 à 2 000 000 ; parallélisme : de 1 à 64." });
            var (registre, comptes) = Imports.BacASable();
            var options = new OptionsImport(r.ParallelismeFraude, r.Ordonne, r.CoutScoring ?? configuration.Value.CoutScoring);

            var bilan = await Imports.Executer(registre, options).Executer(Imports.FichierDeDemo(r.Lignes, comptes), annulation: annulation);

            return Results.Ok(new
            {
                bilan.Statut, bilan.Lignes, executes = bilan.Sorties, bilan.Rejets, bilan.RejetsParMotif,
                bilan.DureeMs, bilan.DebitParSeconde, bilan.MaxEnTransit, bilan.Goulot, bilan.Etages
            });
        });
}
