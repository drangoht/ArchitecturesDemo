using System.Collections.Concurrent;

namespace Ddd.Compliance;

public enum AlertLevel { High, Critical }

public record Alert(Guid Id, Guid AccountId, string Owner, AlertLevel Level, string Reason, DateTime Date);

/// <summary>Value object of THIS context: Compliance reasons in euros, the original currency does not matter to it.</summary>
public record ObservedTransfer(DateTime Date, decimal AmountInEuros, string BeneficiaryIban);

/// <summary>
/// Aggregate of the Compliance context. It represents the same account as Account keeping,
/// but with a completely different model: no balance, no limit, observed transfers and alerts.
/// </summary>
public class VigilanceFile(Guid accountId, string owner)
{
    public const decimal ReportingThreshold = 10_000m;
    public static readonly TimeSpan StructuringWindow = TimeSpan.FromMinutes(10);
    public const int MaxTransfersInWindow = 3;

    private readonly List<ObservedTransfer> _transfers = [];
    private readonly List<Alert> _alerts = [];

    public Guid AccountId { get; } = accountId;
    public string Owner { get; } = owner;
    public bool BlockRequested { get; private set; }
    public IReadOnlyList<ObservedTransfer> Transfers => _transfers;
    public IReadOnlyList<Alert> Alerts => _alerts;

    /// <summary>Records a transfer and returns the alerts it triggers.</summary>
    public IReadOnlyList<Alert> Observe(ObservedTransfer transfer)
    {
        _transfers.Add(transfer);
        var newAlerts = new List<Alert>();

        if (transfer.AmountInEuros >= ReportingThreshold)
            newAlerts.Add(NewAlert(AlertLevel.High, transfer.Date,
                $"Transfer of {transfer.AmountInEuros:N2} € above the reporting threshold ({ReportingThreshold:N0} €)."));

        var close = _transfers.Count(v => v.Date > transfer.Date - StructuringWindow && v.Date <= transfer.Date);
        if (close >= MaxTransfersInWindow && !BlockRequested)
        {
            newAlerts.Add(NewAlert(AlertLevel.Critical, transfer.Date,
                $"{close} transfers in less than {StructuringWindow.TotalMinutes:0} minutes: structuring suspected."));
            BlockRequested = true;
        }

        _alerts.AddRange(newAlerts);
        return newAlerts;
    }

    private Alert NewAlert(AlertLevel level, DateTime date, string reason) =>
        new(Guid.NewGuid(), AccountId, Owner, level, reason, date);
}

/// <summary>The storage specific to Compliance (in memory for the demo). It shares nothing with Account keeping.</summary>
public class VigilanceFiles
{
    private readonly ConcurrentDictionary<Guid, VigilanceFile> _files = new();

    public VigilanceFile Get(Guid accountId, string owner) =>
        _files.GetOrAdd(accountId, id => new VigilanceFile(id, owner));

    public VigilanceFile? Find(Guid accountId) => _files.GetValueOrDefault(accountId);

    public IEnumerable<VigilanceFile> All() => _files.Values;
}
