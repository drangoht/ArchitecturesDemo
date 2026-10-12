using System.Globalization;
using System.Text;

namespace Fig.Modern;

/// <summary>
/// The legacy system's HISTORICAL CONTRACT, reproduced identically: same fields, same order, same date format (Paris
/// time without time zone), same amounts, same statement to the character. It is not the modern service's model, it is
/// what the clients expect; the facade's "shadow" mode checks that it is respected.
/// </summary>
public static class Contract
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    public static object Account(Account a) => new { number = a.Number, owner = a.Owner, balance = Euros(a.Balance) };

    public static object Operations(Account a) => a.Operations
        .Select((o, rank) => (o, rank))
        .OrderByDescending(x => x.o.Date).ThenByDescending(x => x.rank)
        .Select(x => new { date = ParisTime(x.o.Date).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), label = x.o.Label, amount = Euros(x.o.Amount) })
        .ToList();

    public static object Transfer(TransferResult r) => new { reference = r.Reference, sourceBalance = Euros(r.SourceBalance) };

    public static string Statement(Account a, DateTime period)
    {
        var lines = a.Operations
            .Select((o, rank) => (o, rank, paris: ParisTime(o.Date)))
            .Where(x => x.paris.Year == period.Year && x.paris.Month == period.Month)
            .OrderBy(x => x.paris).ThenBy(x => x.rank).ToList();
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"STATEMENT OF ACCOUNT {a.Number} - {a.Owner} - PERIOD {period:MM/yyyy}\r\n");
        foreach (var (o, _, paris) in lines)
            text.Append(CultureInfo.InvariantCulture, $"{paris:dd/MM/yyyy}  {o.Label,-30}  {Amount(o.Amount),12}\r\n");
        text.Append($"{"TOTAL OF MOVEMENTS",-42}  {Amount(lines.Sum(x => x.o.Amount)),12}\r\n");
        text.Append($"{"CURRENT BALANCE",-42}  {Amount(a.Balance),12}\r\n");
        return text.ToString();
    }

    /// <summary>The requested month ("yyyy-MM"), or the current month in Paris.</summary>
    public static DateTime Period(string? month, DateTimeOffset now) =>
        month is null
            ? new DateTime(ParisTime(now).Year, ParisTime(now).Month, 1)
            : DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                ? m
                : throw new BusinessError(400, "INVALID PERIOD");

    // The legacy system computes in cents then divides by 100: 950 and not 950.00 in the JSON. We reproduce its representation.
    private static decimal Euros(decimal amount) => LegacyTranslator.Cents(amount) / 100m;

    private static string Amount(decimal amount) => Euros(amount).ToString("0.00", British);

    private static DateTime ParisTime(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, LegacyTranslator.Paris).DateTime;
}
