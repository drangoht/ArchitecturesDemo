using System.Globalization;
using Pipeline.Core;

namespace Pipeline.Bank;

// The filters of the import of a transfer file, in the order in which they are assembled (see Imports).
// Each one does ONE thing; none knows the others.

/// <summary>A CSV line "reference;payer;payee;amount;currency" → a transfer.</summary>
public sealed class Parse : IFilter<string, Transfer>
{
    public string Name => "Parse";

    public ValueTask<Outcome<Transfer>> Process(string line, CancellationToken cancellation)
    {
        var fields = line.Split(';');
        if (fields.Length != 5)
            return Reject($"{fields.Length} fields instead of 5");
        if (!decimal.TryParse(fields[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return Reject($"unreadable amount \"{fields[3]}\"");
        var c = fields.Select(x => x.Trim()).ToArray();
        return ValueTask.FromResult(Outcome<Transfer>.Ok(new Transfer(c[0], c[1].ToUpperInvariant(), c[2].ToUpperInvariant(), amount, c[4].ToUpperInvariant())));
    }

    private static ValueTask<Outcome<Transfer>> Reject(string detail) => ValueTask.FromResult(Outcome<Transfer>.Reject("malformed line", detail));
}

/// <summary>The rules that only need the line itself.</summary>
public sealed class Validate : IFilter<Transfer, Transfer>
{
    public string Name => "Validate";

    public ValueTask<Outcome<Transfer>> Process(Transfer v, CancellationToken cancellation) => ValueTask.FromResult(
        string.IsNullOrEmpty(v.Reference) ? Outcome<Transfer>.Reject("missing reference")
        : !Iban.IsValid(v.Payer) ? Outcome<Transfer>.Reject("invalid IBAN", v.Payer)
        : !Iban.IsValid(v.Payee) ? Outcome<Transfer>.Reject("invalid IBAN", v.Payee)
        : v.Payer == v.Payee ? Outcome<Transfer>.Reject("identical accounts")
        : v.Amount <= 0 ? Outcome<Transfer>.Reject("invalid amount", v.Amount.ToString(CultureInfo.InvariantCulture))
        : Outcome<Transfer>.Ok(v));
}

/// <summary>
/// Discards a reference already seen in this file, or already executed by a previous import: replaying a file
/// (after a failure, a handling mistake) executes nothing twice. STATEFUL filter: a single copy, and a
/// new pipeline per import.
/// </summary>
public sealed class Deduplicate(Ledger ledger) : IFilter<Transfer, Transfer>
{
    private readonly HashSet<string> _seen = new();

    public string Name => "Deduplicate";

    public ValueTask<Outcome<Transfer>> Process(Transfer v, CancellationToken cancellation) => ValueTask.FromResult(
        !_seen.Add(v.Reference) ? Outcome<Transfer>.Reject("duplicate", "in the file")
        : ledger.AlreadyExecuted(v.Reference) ? Outcome<Transfer>.Reject("duplicate", "already executed")
        : Outcome<Transfer>.Ok(v));
}

/// <summary>Amount converted into euros.</summary>
public sealed class ConvertCurrency : IFilter<Transfer, Transfer>
{
    private static readonly Dictionary<string, decimal> Rates = new() { ["EUR"] = 1m, ["USD"] = 0.92m, ["GBP"] = 1.17m, ["CHF"] = 1.04m };

    public string Name => "Convert currency";

    public ValueTask<Outcome<Transfer>> Process(Transfer v, CancellationToken cancellation) => ValueTask.FromResult(
        Rates.TryGetValue(v.Currency, out var rate)
            ? Outcome<Transfer>.Ok(v with { AmountEur = Math.Round(v.Amount * rate, 2) })
            : Outcome<Transfer>.Reject("unknown currency", v.Currency));
}

/// <summary>
/// The fraud check: an expensive score computation (simulated: <paramref name="cost"/> × 1,000 rounds of an FNV hash),
/// a ceiling and a blocklist. Stateless: several copies can run in parallel, it is the slowest stage.
/// </summary>
public sealed class CheckFraud(int cost, decimal ceiling = 10_000m) : IFilter<Transfer, Transfer>
{
    private static readonly HashSet<string> Blocklist = [DemoData.Suspect];

    public string Name => "Check fraud";

    public ValueTask<Outcome<Transfer>> Process(Transfer v, CancellationToken cancellation)
    {
        // Pure computation, without allocation: it really runs in parallel on several cores.
        var score = 14695981039346656037ul;
        foreach (var c in v.Reference)
            score = (score ^ c) * 1099511628211ul;
        for (var i = 0; i < cost * 1_000; i++)
            score = (score ^ (ulong)i) * 1099511628211ul;

        return ValueTask.FromResult(
            score == 0 ? Outcome<Transfer>.Reject("abnormal score")
            : Blocklist.Contains(v.Payee) ? Outcome<Transfer>.Reject("payee on blocklist", v.Payee)
            : v.AmountEur > ceiling ? Outcome<Transfer>.Reject("ceiling exceeded", $"{v.AmountEur:0.00} €")
            : Outcome<Transfer>.Ok(v));
    }
}

/// <summary>The last filter: it debits and credits. STATEFUL (the balances): a single copy, in the file's order.</summary>
public sealed class Execute(Ledger ledger) : IFilter<Transfer, ExecutedTransfer>
{
    public string Name => "Execute";

    public ValueTask<Outcome<ExecutedTransfer>> Process(Transfer v, CancellationToken cancellation) => ValueTask.FromResult(
        ledger.Transfer(v, out var balance) is { } reason
            ? Outcome<ExecutedTransfer>.Reject(reason, v.Reference)
            : Outcome<ExecutedTransfer>.Ok(new ExecutedTransfer(v.Reference, v.Payer, v.Payee, v.AmountEur, balance)));
}

/// <summary>A failure in the middle of an import (a bug, a service down): an exception, not a rejection. The whole pipeline stops.</summary>
public sealed class SimulatedFailure(string reference) : IFilter<Transfer, Transfer>
{
    public string Name => "Simulated failure";

    public ValueTask<Outcome<Transfer>> Process(Transfer v, CancellationToken cancellation) =>
        v.Reference == reference
            ? throw new InvalidOperationException($"Simulated failure while processing {reference}.")
            : ValueTask.FromResult(Outcome<Transfer>.Ok(v));
}
