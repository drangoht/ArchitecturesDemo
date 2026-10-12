using System.Globalization;
using System.Text;
using Mk.Contracts;

namespace Mk.Plugins.Exports;

/// <summary>"Statement format" extension point: CSV, for a spreadsheet.</summary>
public sealed class CsvExport : IStatementFormat
{
    public string Name => "CsvExport";
    public string Description => "Statement in CSV format (semicolon separator).";
    public string Format => "csv";
    public string MimeType => "text/csv";

    public string Produce(AccountStatement statement)
    {
        var csv = new StringBuilder("date;label;amount;balance\n");
        foreach (var line in statement.Lines)
            csv.Append(CultureInfo.InvariantCulture,
                $"{line.Date:yyyy-MM-dd HH:mm};{line.Label};{line.Amount:0.00};{line.BalanceAfter:0.00}\n");
        return csv.ToString();
    }
}

/// <summary>"Statement format" extension point: QIF, the historic exchange format of personal finance software.</summary>
public sealed class QifExport : IStatementFormat
{
    public string Name => "QifExport";
    public string Description => "Statement in QIF format (Quicken Interchange Format).";
    public string Format => "qif";
    public string MimeType => "application/qif";

    public string Produce(AccountStatement statement)
    {
        var qif = new StringBuilder("!Type:Bank\n");
        foreach (var line in statement.Lines)
            qif.Append(CultureInfo.InvariantCulture, $"D{line.Date:MM/dd/yyyy}\nT{line.Amount:0.00}\nP{line.Label}\n^\n");
        return qif.ToString();
    }
}
