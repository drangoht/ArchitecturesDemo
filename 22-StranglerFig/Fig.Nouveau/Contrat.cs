using System.Globalization;
using System.Text;

namespace Fig.Nouveau;

/// <summary>
/// Le CONTRAT HISTORIQUE de l'ancien, reproduit à l'identique : mêmes champs, même ordre, même format de date (heure de
/// Paris sans fuseau), mêmes montants, même relevé au caractère près. Ce n'est pas le modèle du nouveau service, c'est
/// ce que les clients attendent ; le mode « ombre » de la façade vérifie qu'on le respecte.
/// </summary>
public static class Contrat
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public static object Compte(Compte c) => new { numero = c.Numero, titulaire = c.Titulaire, solde = Euros(c.Solde) };

    public static object Operations(Compte c) => c.Operations
        .Select((o, rang) => (o, rang))
        .OrderByDescending(x => x.o.Date).ThenByDescending(x => x.rang)
        .Select(x => new { date = HeureDeParis(x.o.Date).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), libelle = x.o.Libelle, montant = Euros(x.o.Montant) })
        .ToList();

    public static object Virement(ResultatVirement r) => new { reference = r.Reference, soldeSource = Euros(r.SoldeSource) };

    public static string Releve(Compte c, DateTime periode)
    {
        var lignes = c.Operations
            .Select((o, rang) => (o, rang, paris: HeureDeParis(o.Date)))
            .Where(x => x.paris.Year == periode.Year && x.paris.Month == periode.Month)
            .OrderBy(x => x.paris).ThenBy(x => x.rang).ToList();
        var texte = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"RELEVE DU COMPTE {c.Numero} - {c.Titulaire} - PERIODE {periode:MM/yyyy}\r\n");
        foreach (var (o, _, paris) in lignes)
            texte.Append(CultureInfo.InvariantCulture, $"{paris:dd/MM/yyyy}  {o.Libelle,-30}  {Montant(o.Montant),12}\r\n");
        texte.Append($"{"TOTAL DES MOUVEMENTS",-42}  {Montant(lignes.Sum(x => x.o.Montant)),12}\r\n");
        texte.Append($"{"SOLDE ACTUEL",-42}  {Montant(c.Solde),12}\r\n");
        return texte.ToString();
    }

    /// <summary>Le mois demandé (« aaaa-MM »), ou le mois en cours à Paris.</summary>
    public static DateTime Periode(string? mois, DateTimeOffset maintenant) =>
        mois is null
            ? new DateTime(HeureDeParis(maintenant).Year, HeureDeParis(maintenant).Month, 1)
            : DateTime.TryParseExact(mois, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                ? m
                : throw new ErreurMetier(400, "PERIODE INVALIDE");

    // L'ancien calcule en centimes puis divise par 100 : 950 et non 950.00 dans le JSON. On reproduit sa représentation.
    private static decimal Euros(decimal montant) => TraducteurAncien.Centimes(montant) / 100m;

    private static string Montant(decimal montant) => Euros(montant).ToString("0.00", Francais);

    private static DateTime HeureDeParis(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, TraducteurAncien.Paris).DateTime;
}
