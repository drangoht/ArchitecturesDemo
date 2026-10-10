using System.Globalization;
using System.Text;
using Mk.Contrats;

namespace Mk.Plugins.Exports;

/// <summary>Point d'extension « format de relevé » : CSV, pour un tableur.</summary>
public sealed class ExportCsv : IFormatReleve
{
    public string Nom => "ExportCsv";
    public string Description => "Relevé au format CSV (séparateur point-virgule).";
    public string Format => "csv";
    public string TypeMime => "text/csv";

    public string Produire(ReleveCompte releve)
    {
        var csv = new StringBuilder("date;libelle;montant;solde\n");
        foreach (var ligne in releve.Lignes)
            csv.Append(CultureInfo.InvariantCulture,
                $"{ligne.Date:yyyy-MM-dd HH:mm};{ligne.Libelle};{ligne.Montant:0.00};{ligne.SoldeApres:0.00}\n");
        return csv.ToString();
    }
}

/// <summary>Point d'extension « format de relevé » : QIF, le format d'échange historique des logiciels de comptes.</summary>
public sealed class ExportQif : IFormatReleve
{
    public string Nom => "ExportQif";
    public string Description => "Relevé au format QIF (Quicken Interchange Format).";
    public string Format => "qif";
    public string TypeMime => "application/qif";

    public string Produire(ReleveCompte releve)
    {
        var qif = new StringBuilder("!Type:Bank\n");
        foreach (var ligne in releve.Lignes)
            qif.Append(CultureInfo.InvariantCulture, $"D{ligne.Date:MM/dd/yyyy}\nT{ligne.Montant:0.00}\nP{ligne.Libelle}\n^\n");
        return qif.ToString();
    }
}
