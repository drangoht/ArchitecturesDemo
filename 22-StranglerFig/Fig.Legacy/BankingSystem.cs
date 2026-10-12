using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Fig.Legacy;

/// <summary>Assembly marker, for the tests (each project of the demo has its own Program).</summary>
public sealed class Marker;

// ─── The "tables" of the legacy system ───
// Amounts in CENTS, dates in Paris time WITHOUT time zone, labels in UPPERCASE truncated to 30 characters, error
// messages in uppercase: choices made twenty years ago, on which its clients and batches depend today.
public sealed class AccountRow
{
    public string Number = "";
    public string Owner = "";
    public long BalanceCents;
}

public sealed class OperationRow
{
    public long Sequence;
    public string OperationId = "";
    public string Number = "";
    public DateTime Date;          // Paris time, Kind = Unspecified
    public string Label = "";
    public long AmountCents;
    public string Origin = "";     // LEGACY, or MODERN for an operation imported from the modern service
}

/// <summary>
/// A row of the change log (added for the migration, like a database trigger):
/// the modern service reads it to stay up to date. The column names are those of the legacy database.
/// </summary>
public sealed record LogRow(
    [property: JsonPropertyName("POS")] long Position,
    [property: JsonPropertyName("TYP")] string Type,              // OPENING or OPERATION
    [property: JsonPropertyName("ACCNUM")] string Number,
    [property: JsonPropertyName("CUSTNM")] string? Owner,
    [property: JsonPropertyName("OPEID")] string? OperationId,
    [property: JsonPropertyName("OPEDAT")] string? Date,          // "dd/MM/yyyy HH:mm:ss", Paris time
    [property: JsonPropertyName("OPELBL")] string? Label,
    [property: JsonPropertyName("AMTCTS")] long AmountCents,
    [property: JsonPropertyName("ORIG")] string Origin);

/// <summary>An operation written by the modern service, which the legacy system copies at home (same format as its log).</summary>
public sealed record ImportedOperation(
    [property: JsonPropertyName("OPEID")] string OperationId,
    [property: JsonPropertyName("ACCNUM")] string Number,
    [property: JsonPropertyName("OPEDAT")] string Date,
    [property: JsonPropertyName("OPELBL")] string Label,
    [property: JsonPropertyName("AMTCTS")] long AmountCents);

public sealed record FeeRequest(
    [property: JsonPropertyName("ACCNUM")] string Number,
    [property: JsonPropertyName("AMTCTS")] long AmountCents,
    [property: JsonPropertyName("OPELBL")] string Label);

public sealed record TransferRequest(string Source, string Target, decimal Amount);

public sealed class BusinessError(int status, string message) : Exception(message)
{
    public int Status => status;
}

/// <summary>
/// The whole legacy system in one class: a global lock, lists, and the rules mixed with the data format.
/// We do not rewrite it: we bypass it, one route after the other, until it can be switched off.
/// </summary>
public sealed class BankingSystem
{
    public const string DateFormat = "dd/MM/yyyy HH:mm:ss";
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    private readonly object _lock = new();
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, AccountRow> _accounts = [];
    private readonly List<OperationRow> _operations = [];
    private readonly List<LogRow> _log = [];
    private long _sequence;
    private long _transferNumber;

    public BankingSystem(TimeProvider clock)
    {
        _clock = clock;
        lock (_lock)
        {
            Open("10001", "ALICE MARTIN", 1_000_00);
            Open("10002", "BOB DURAND", 500_00);
        }
    }

    public AccountRow Account(string number)
    {
        lock (_lock)
        {
            var account = Find(number);
            return new AccountRow { Number = account.Number, Owner = account.Owner, BalanceCents = account.BalanceCents };
        }
    }

    /// <summary>The operations, the most recent first (for equal dates: the last recorded first).</summary>
    public List<OperationRow> Operations(string number)
    {
        lock (_lock)
        {
            Find(number);
            return _operations.Where(o => o.Number == number)
                .OrderByDescending(o => o.Date).ThenByDescending(o => o.Sequence).ToList();
        }
    }

    public (string Reference, long SourceBalanceCents) Transfer(string source, string target, decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new BusinessError(400, "INVALID AMOUNT");
        if (source == target)
            throw new BusinessError(400, "IDENTICAL ACCOUNTS");
        var cents = (long)(amount * 100);
        lock (_lock)
        {
            var debited = Find(source);
            var credited = Find(target);
            if (debited.BalanceCents < cents)
                throw new BusinessError(400, "INSUFFICIENT BALANCE");
            var reference = $"TRF-{++_transferNumber:D6}";
            var date = Now();
            Record(debited, $"L-{_sequence + 1:D6}", date, $"TRF TO {target}", -cents, "LEGACY");
            Record(credited, $"L-{_sequence + 1:D6}", date, $"TRF FROM {source}", cents, "LEGACY");
            return (reference, debited.BalanceCents);
        }
    }

    /// <summary>
    /// An internal batch (the fees, at night): it is not exposed to the clients, will never go through
    /// the facade, and keeps writing into the legacy system during the whole migration.
    /// </summary>
    public void ChargeFee(FeeRequest fee)
    {
        lock (_lock)
            Record(Find(fee.Number), $"L-{_sequence + 1:D6}", Now(), fee.Label, -fee.AmountCents, "LEGACY");
    }

    /// <summary>
    /// Copies the operations written by the modern service. Idempotent: an operation already received is ignored, the
    /// modern service can therefore send again a batch it did not get the confirmation for.
    /// </summary>
    public int Import(IReadOnlyList<ImportedOperation> operations)
    {
        lock (_lock)
        {
            var imported = 0;
            foreach (var o in operations)
            {
                if (_operations.Any(x => x.OperationId == o.OperationId))
                    continue;
                var date = DateTime.ParseExact(o.Date, DateFormat, CultureInfo.InvariantCulture);
                Record(Find(o.Number), o.OperationId, date, o.Label, o.AmountCents, "MODERN");
                imported++;
            }
            return imported;
        }
    }

    public List<LogRow> Log(long since, int max)
    {
        lock (_lock)
            return _log.Where(l => l.Position > since).Take(max).ToList();
    }

    public string Statement(string number, string? month)
    {
        var period = month is null
            ? new DateTime(Now().Year, Now().Month, 1)
            : DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                ? m
                : throw new BusinessError(400, "INVALID PERIOD");
        lock (_lock)
        {
            var account = Find(number);
            var lines = _operations
                .Where(o => o.Number == number && o.Date.Year == period.Year && o.Date.Month == period.Month)
                .OrderBy(o => o.Date).ThenBy(o => o.Sequence).ToList();
            var text = new StringBuilder()
                .Append(CultureInfo.InvariantCulture, $"STATEMENT OF ACCOUNT {account.Number} - {account.Owner} - PERIOD {period:MM/yyyy}\r\n");
            foreach (var o in lines)
                text.Append(CultureInfo.InvariantCulture, $"{o.Date:dd/MM/yyyy}  {o.Label,-30}  {Amount(o.AmountCents),12}\r\n");
            text.Append($"{"TOTAL OF MOVEMENTS",-42}  {Amount(lines.Sum(o => o.AmountCents)),12}\r\n");
            text.Append($"{"CURRENT BALANCE",-42}  {Amount(account.BalanceCents),12}\r\n");
            return text.ToString();
        }
    }

    public static string Amount(long cents) => (cents / 100m).ToString("0.00", British);

    public static string Label(string text)
    {
        var uppercase = text.Trim().ToUpperInvariant();
        return uppercase.Length <= 30 ? uppercase : uppercase[..30];
    }

    private void Open(string number, string owner, long initialDeposit)
    {
        var account = new AccountRow { Number = number, Owner = owner };
        _accounts[number] = account;
        _log.Add(new LogRow(_log.Count + 1, "OPENING", number, owner, null, null, null, 0, "LEGACY"));
        Record(account, $"L-{_sequence + 1:D6}", Now(), "INITIAL DEPOSIT", initialDeposit, "LEGACY");
    }

    private void Record(AccountRow account, string id, DateTime date, string label, long cents, string origin)
    {
        var operation = new OperationRow
        {
            Sequence = ++_sequence, OperationId = id, Number = account.Number, Date = date,
            Label = Label(label), AmountCents = cents, Origin = origin
        };
        _operations.Add(operation);
        account.BalanceCents += cents;
        _log.Add(new LogRow(_log.Count + 1, "OPERATION", account.Number, null, id,
            date.ToString(DateFormat, CultureInfo.InvariantCulture), operation.Label, cents, origin));
    }

    private AccountRow Find(string number) =>
        _accounts.GetValueOrDefault(number) ?? throw new BusinessError(404, "UNKNOWN ACCOUNT");

    private DateTime Now()
    {
        var paris = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Paris).DateTime;
        return new DateTime(paris.Ticks - paris.Ticks % TimeSpan.TicksPerSecond);   // to the second, like the legacy database
    }
}
