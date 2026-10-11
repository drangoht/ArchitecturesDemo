using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Fig.Ancien;

/// <summary>Repère de l'assemblage, pour les tests (chaque projet de la démo a son propre Program).</summary>
public sealed class Marqueur;

// ─── Les « tables » de l'ancien système ───
// Montants en CENTIMES, dates à l'heure de Paris SANS fuseau, libellés en MAJUSCULES tronqués à 30 caractères, messages
// d'erreur en majuscules : des choix d'il y a vingt ans, dont dépendent aujourd'hui ses clients et ses traitements.
public sealed class LigneCompte
{
    public string Numero = "";
    public string Titulaire = "";
    public long SoldeCentimes;
}

public sealed class LigneOperation
{
    public long Sequence;
    public string IdOperation = "";
    public string Numero = "";
    public DateTime Date;          // heure de Paris, Kind = Unspecified
    public string Libelle = "";
    public long MontantCentimes;
    public string Origine = "";    // ANCIEN, ou NOUVEAU pour une opération importée du nouveau service
}

/// <summary>
/// Une ligne du journal des modifications (ajouté pour la migration, à la manière d'un déclencheur de base de données) :
/// le nouveau service le lit pour rester à jour. Les noms de colonnes sont ceux de l'ancienne base.
/// </summary>
public sealed record LigneJournal(
    [property: JsonPropertyName("POS")] long Position,
    [property: JsonPropertyName("TYP")] string Type,              // OUVERTURE ou OPERATION
    [property: JsonPropertyName("NUMCPT")] string Numero,
    [property: JsonPropertyName("LIBCLI")] string? Titulaire,
    [property: JsonPropertyName("IDOPE")] string? IdOperation,
    [property: JsonPropertyName("DATOPE")] string? Date,          // « jj/MM/aaaa HH:mm:ss », heure de Paris
    [property: JsonPropertyName("LIBOPE")] string? Libelle,
    [property: JsonPropertyName("MNTCTS")] long MontantCentimes,
    [property: JsonPropertyName("ORIG")] string Origine);

/// <summary>Une opération écrite par le nouveau service, que l'ancien recopie chez lui (même format que son journal).</summary>
public sealed record OperationImportee(
    [property: JsonPropertyName("IDOPE")] string IdOperation,
    [property: JsonPropertyName("NUMCPT")] string Numero,
    [property: JsonPropertyName("DATOPE")] string Date,
    [property: JsonPropertyName("LIBOPE")] string Libelle,
    [property: JsonPropertyName("MNTCTS")] long MontantCentimes);

public sealed record DemandeFrais(
    [property: JsonPropertyName("NUMCPT")] string Numero,
    [property: JsonPropertyName("MNTCTS")] long MontantCentimes,
    [property: JsonPropertyName("LIBOPE")] string Libelle);

public sealed record DemandeVirement(string Source, string Cible, decimal Montant);

public sealed class ErreurMetier(int statut, string message) : Exception(message)
{
    public int Statut => statut;
}

/// <summary>
/// Tout l'ancien système dans une classe : un verrou global, des listes, et les règles mêlées au format des données.
/// On ne le réécrit pas : on le contourne, une route après l'autre, jusqu'à pouvoir l'éteindre.
/// </summary>
public sealed class SystemeBancaire
{
    public const string FormatDate = "dd/MM/yyyy HH:mm:ss";
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    private readonly object _verrou = new();
    private readonly TimeProvider _horloge;
    private readonly Dictionary<string, LigneCompte> _comptes = [];
    private readonly List<LigneOperation> _operations = [];
    private readonly List<LigneJournal> _journal = [];
    private long _sequence;
    private long _numeroVirement;

    public SystemeBancaire(TimeProvider horloge)
    {
        _horloge = horloge;
        lock (_verrou)
        {
            Ouvrir("10001", "ALICE MARTIN", 1_000_00);
            Ouvrir("10002", "BOB DURAND", 500_00);
        }
    }

    public LigneCompte Compte(string numero)
    {
        lock (_verrou)
        {
            var compte = Trouver(numero);
            return new LigneCompte { Numero = compte.Numero, Titulaire = compte.Titulaire, SoldeCentimes = compte.SoldeCentimes };
        }
    }

    /// <summary>Les opérations, les plus récentes d'abord (à date égale : la dernière enregistrée d'abord).</summary>
    public List<LigneOperation> Operations(string numero)
    {
        lock (_verrou)
        {
            Trouver(numero);
            return _operations.Where(o => o.Numero == numero)
                .OrderByDescending(o => o.Date).ThenByDescending(o => o.Sequence).ToList();
        }
    }

    public (string Reference, long SoldeSourceCentimes) Virer(string source, string cible, decimal montant)
    {
        if (montant <= 0 || decimal.Round(montant, 2) != montant)
            throw new ErreurMetier(400, "MONTANT INVALIDE");
        if (source == cible)
            throw new ErreurMetier(400, "COMPTES IDENTIQUES");
        var centimes = (long)(montant * 100);
        lock (_verrou)
        {
            var debite = Trouver(source);
            var credite = Trouver(cible);
            if (debite.SoldeCentimes < centimes)
                throw new ErreurMetier(400, "SOLDE INSUFFISANT");
            var reference = $"VIR-{++_numeroVirement:D6}";
            var date = Maintenant();
            Enregistrer(debite, $"A-{_sequence + 1:D6}", date, $"VIR VERS {cible}", -centimes, "ANCIEN");
            Enregistrer(credite, $"A-{_sequence + 1:D6}", date, $"VIR DE {source}", centimes, "ANCIEN");
            return (reference, debite.SoldeCentimes);
        }
    }

    /// <summary>
    /// Un traitement interne (le passage des frais, la nuit) : il n'est pas exposé aux clients, ne passera jamais par
    /// la façade, et continue d'écrire dans l'ancien pendant toute la migration.
    /// </summary>
    public void PreleverFrais(DemandeFrais frais)
    {
        lock (_verrou)
            Enregistrer(Trouver(frais.Numero), $"A-{_sequence + 1:D6}", Maintenant(), frais.Libelle, -frais.MontantCentimes, "ANCIEN");
    }

    /// <summary>
    /// Recopie les opérations écrites par le nouveau service. Idempotent : une opération déjà reçue est ignorée, le
    /// nouveau service peut donc renvoyer un lot dont il n'a pas eu la confirmation.
    /// </summary>
    public int Importer(IReadOnlyList<OperationImportee> operations)
    {
        lock (_verrou)
        {
            var importees = 0;
            foreach (var o in operations)
            {
                if (_operations.Any(x => x.IdOperation == o.IdOperation))
                    continue;
                var date = DateTime.ParseExact(o.Date, FormatDate, CultureInfo.InvariantCulture);
                Enregistrer(Trouver(o.Numero), o.IdOperation, date, o.Libelle, o.MontantCentimes, "NOUVEAU");
                importees++;
            }
            return importees;
        }
    }

    public List<LigneJournal> Journal(long depuis, int max)
    {
        lock (_verrou)
            return _journal.Where(l => l.Position > depuis).Take(max).ToList();
    }

    public string Releve(string numero, string? mois)
    {
        var periode = mois is null
            ? new DateTime(Maintenant().Year, Maintenant().Month, 1)
            : DateTime.TryParseExact(mois, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                ? m
                : throw new ErreurMetier(400, "PERIODE INVALIDE");
        lock (_verrou)
        {
            var compte = Trouver(numero);
            var lignes = _operations
                .Where(o => o.Numero == numero && o.Date.Year == periode.Year && o.Date.Month == periode.Month)
                .OrderBy(o => o.Date).ThenBy(o => o.Sequence).ToList();
            var texte = new StringBuilder()
                .Append(CultureInfo.InvariantCulture, $"RELEVE DU COMPTE {compte.Numero} - {compte.Titulaire} - PERIODE {periode:MM/yyyy}\r\n");
            foreach (var o in lignes)
                texte.Append(CultureInfo.InvariantCulture, $"{o.Date:dd/MM/yyyy}  {o.Libelle,-30}  {Montant(o.MontantCentimes),12}\r\n");
            texte.Append($"{"TOTAL DES MOUVEMENTS",-42}  {Montant(lignes.Sum(o => o.MontantCentimes)),12}\r\n");
            texte.Append($"{"SOLDE ACTUEL",-42}  {Montant(compte.SoldeCentimes),12}\r\n");
            return texte.ToString();
        }
    }

    public static string Montant(long centimes) => (centimes / 100m).ToString("0.00", Francais);

    public static string Libelle(string texte)
    {
        var majuscules = texte.Trim().ToUpperInvariant();
        return majuscules.Length <= 30 ? majuscules : majuscules[..30];
    }

    private void Ouvrir(string numero, string titulaire, long depotInitial)
    {
        var compte = new LigneCompte { Numero = numero, Titulaire = titulaire };
        _comptes[numero] = compte;
        _journal.Add(new LigneJournal(_journal.Count + 1, "OUVERTURE", numero, titulaire, null, null, null, 0, "ANCIEN"));
        Enregistrer(compte, $"A-{_sequence + 1:D6}", Maintenant(), "VERSEMENT INITIAL", depotInitial, "ANCIEN");
    }

    private void Enregistrer(LigneCompte compte, string id, DateTime date, string libelle, long centimes, string origine)
    {
        var operation = new LigneOperation
        {
            Sequence = ++_sequence, IdOperation = id, Numero = compte.Numero, Date = date,
            Libelle = Libelle(libelle), MontantCentimes = centimes, Origine = origine
        };
        _operations.Add(operation);
        compte.SoldeCentimes += centimes;
        _journal.Add(new LigneJournal(_journal.Count + 1, "OPERATION", compte.Numero, null, id,
            date.ToString(FormatDate, CultureInfo.InvariantCulture), operation.Libelle, centimes, origine));
    }

    private LigneCompte Trouver(string numero) =>
        _comptes.GetValueOrDefault(numero) ?? throw new ErreurMetier(404, "COMPTE INCONNU");

    private DateTime Maintenant()
    {
        var paris = TimeZoneInfo.ConvertTime(_horloge.GetUtcNow(), Paris).DateTime;
        return new DateTime(paris.Ticks - paris.Ticks % TimeSpan.TicksPerSecond);   // à la seconde, comme l'ancienne base
    }
}
