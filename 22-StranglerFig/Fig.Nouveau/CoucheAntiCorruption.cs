using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Fig.Nouveau;

// ─── Le modèle de l'ANCIEN, tel qu'il arrive par HTTP. Il ne sort jamais de ce fichier. ───

public sealed record LigneJournalAncien(
    [property: JsonPropertyName("POS")] long Position,
    [property: JsonPropertyName("TYP")] string Type,
    [property: JsonPropertyName("NUMCPT")] string Numero,
    [property: JsonPropertyName("LIBCLI")] string? Titulaire,
    [property: JsonPropertyName("IDOPE")] string? IdOperation,
    [property: JsonPropertyName("DATOPE")] string? Date,
    [property: JsonPropertyName("LIBOPE")] string? Libelle,
    [property: JsonPropertyName("MNTCTS")] long MontantCentimes,
    [property: JsonPropertyName("ORIG")] string Origine);

public sealed record OperationPourAncien(
    [property: JsonPropertyName("IDOPE")] string IdOperation,
    [property: JsonPropertyName("NUMCPT")] string Numero,
    [property: JsonPropertyName("DATOPE")] string Date,
    [property: JsonPropertyName("LIBOPE")] string Libelle,
    [property: JsonPropertyName("MNTCTS")] long MontantCentimes);

/// <summary>
/// La COUCHE ANTI-CORRUPTION : traduit dans les deux sens entre le modèle de l'ancien (centimes, dates sans fuseau,
/// colonnes à six lettres) et celui du nouveau service. C'est le seul endroit qui connaît les deux ; le jour où l'ancien
/// est éteint, on supprime ce fichier et rien d'autre ne change.
/// </summary>
public static class TraducteurAncien
{
    private const string FormatDate = "dd/MM/yyyy HH:mm:ss";
    public static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    /// <summary>
    /// Une ligne du journal → un fait du nouveau modèle. Les opérations que NOUS avons écrites reviennent aussi, en écho
    /// (ORIG = NOUVEAU), avec notre identifiant : <see cref="Compte"/> les reconnaît et ne les compte pas deux fois.
    /// </summary>
    public static FaitAncien Traduire(LigneJournalAncien ligne) => ligne switch
    {
        { Type: "OUVERTURE" } => new CompteOuvert(ligne.Numero, ligne.Titulaire!),
        { Type: "OPERATION" } => new OperationEnregistree(ligne.Numero, new Operation(
            ligne.IdOperation!, DateDepuisAncien(ligne.Date!), ligne.Libelle!, ligne.MontantCentimes / 100m,
            ligne.Origine == "NOUVEAU" ? Origine.Nouveau : Origine.Ancien)),
        // Un type inconnu arrête la synchronisation plutôt que d'être ignoré : mieux vaut un retard visible qu'un écart silencieux.
        _ => throw new FormatException($"Type de ligne inconnu dans le journal de l'ancien : {ligne.Type}")
    };

    public static OperationPourAncien VersAncien(OperationAReporter a) =>
        new(a.Operation.Id, a.Numero, DateVersAncien(a.Operation.Date), a.Operation.Libelle, Centimes(a.Operation.Montant));

    /// <summary>L'ancien note l'heure de Paris sans fuseau : c'est ici qu'on décide de quel instant il s'agit.</summary>
    public static DateTimeOffset DateDepuisAncien(string texte)
    {
        var locale = DateTime.ParseExact(texte, FormatDate, CultureInfo.InvariantCulture);
        return new DateTimeOffset(locale, Paris.GetUtcOffset(locale));
    }

    public static string DateVersAncien(DateTimeOffset date) =>
        TimeZoneInfo.ConvertTime(date, Paris).ToString(FormatDate, CultureInfo.InvariantCulture);

    public static long Centimes(decimal euros) =>
        decimal.Round(euros, 2) == euros ? (long)(euros * 100) : throw new ArgumentException($"Montant au-delà du centime : {euros}");
}

/// <summary>Le client HTTP de l'ancien, limité aux deux routes internes ajoutées pour la migration.</summary>
public sealed class ClientAncien(HttpClient http)
{
    public async Task<List<LigneJournalAncien>> Journal(long depuis, int max, CancellationToken annulation) =>
        (await http.GetFromJsonAsync<List<LigneJournalAncien>>($"/interne/journal?depuis={depuis}&max={max}", annulation))!;

    public async Task Importer(IReadOnlyList<OperationPourAncien> operations, CancellationToken annulation)
    {
        using var reponse = await http.PostAsJsonAsync("/interne/operations-importees", operations, annulation);
        reponse.EnsureSuccessStatusCode();
    }
}
