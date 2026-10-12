using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Fig.Modern;

// ─── The LEGACY model, as it arrives through HTTP. It never leaves this file. ───

public sealed record LegacyLogRow(
    [property: JsonPropertyName("POS")] long Position,
    [property: JsonPropertyName("TYP")] string Type,
    [property: JsonPropertyName("ACCNUM")] string Number,
    [property: JsonPropertyName("CUSTNM")] string? Owner,
    [property: JsonPropertyName("OPEID")] string? OperationId,
    [property: JsonPropertyName("OPEDAT")] string? Date,
    [property: JsonPropertyName("OPELBL")] string? Label,
    [property: JsonPropertyName("AMTCTS")] long AmountCents,
    [property: JsonPropertyName("ORIG")] string Origin);

public sealed record OperationForLegacy(
    [property: JsonPropertyName("OPEID")] string OperationId,
    [property: JsonPropertyName("ACCNUM")] string Number,
    [property: JsonPropertyName("OPEDAT")] string Date,
    [property: JsonPropertyName("OPELBL")] string Label,
    [property: JsonPropertyName("AMTCTS")] long AmountCents);

/// <summary>
/// The ANTI-CORRUPTION LAYER: translates both ways between the legacy model (cents, dates without time zone,
/// six-letter columns) and the modern service's one. It is the only place that knows both; the day the legacy system
/// is switched off, this file is deleted and nothing else changes.
/// </summary>
public static class LegacyTranslator
{
    private const string DateFormat = "dd/MM/yyyy HH:mm:ss";
    public static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    /// <summary>
    /// A log row → a fact of the modern model. The operations that WE wrote also come back, as an echo
    /// (ORIG = MODERN), with our identifier: <see cref="Account"/> recognizes them and does not count them twice.
    /// </summary>
    public static LegacyFact Translate(LegacyLogRow row) => row switch
    {
        { Type: "OPENING" } => new AccountOpened(row.Number, row.Owner!),
        { Type: "OPERATION" } => new OperationRecorded(row.Number, new Operation(
            row.OperationId!, DateFromLegacy(row.Date!), row.Label!, row.AmountCents / 100m,
            row.Origin == "MODERN" ? Origin.Modern : Origin.Legacy)),
        // An unknown type stops the synchronization rather than being ignored: better a visible delay than a silent gap.
        _ => throw new FormatException($"Unknown row type in the legacy log: {row.Type}")
    };

    public static OperationForLegacy ToLegacy(OperationToForward f) =>
        new(f.Operation.Id, f.Number, DateToLegacy(f.Operation.Date), f.Operation.Label, Cents(f.Operation.Amount));

    /// <summary>The legacy system notes Paris time without time zone: this is where we decide which instant it is.</summary>
    public static DateTimeOffset DateFromLegacy(string text)
    {
        var local = DateTime.ParseExact(text, DateFormat, CultureInfo.InvariantCulture);
        return new DateTimeOffset(local, Paris.GetUtcOffset(local));
    }

    public static string DateToLegacy(DateTimeOffset date) =>
        TimeZoneInfo.ConvertTime(date, Paris).ToString(DateFormat, CultureInfo.InvariantCulture);

    public static long Cents(decimal euros) =>
        decimal.Round(euros, 2) == euros ? (long)(euros * 100) : throw new ArgumentException($"Amount beyond the cent: {euros}");
}

/// <summary>The legacy system's HTTP client, limited to the two internal routes added for the migration.</summary>
public sealed class LegacyClient(HttpClient http)
{
    public async Task<List<LegacyLogRow>> Log(long since, int max, CancellationToken cancellation) =>
        (await http.GetFromJsonAsync<List<LegacyLogRow>>($"/internal/log?since={since}&max={max}", cancellation))!;

    public async Task Import(IReadOnlyList<OperationForLegacy> operations, CancellationToken cancellation)
    {
        using var response = await http.PostAsJsonAsync("/internal/imported-operations", operations, cancellation);
        response.EnsureSuccessStatusCode();
    }
}
