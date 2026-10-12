using System.Globalization;
using System.Runtime.CompilerServices;
using Pipeline.Core;

namespace Pipeline.Bank;

public sealed record ImportOptions(int FraudParallelism = 1, bool Ordered = true, int ScoringCost = 0, string? FailureAtReference = null);

/// <summary>
/// The assembly: the same filters make two pipelines. The import executes the transfers; the "dry run" import
/// stops before the execution and only tells what would be rejected.
/// </summary>
public static class Imports
{
    /// <summary>The checks, common to both pipelines. The cheap filters first: what they reject does not reach the expensive ones.</summary>
    private static Builder<string, Transfer> Checks(Ledger ledger, ImportOptions options) =>
        Core.Pipeline.From<string>()
            .Then(new Parse())
            .Then(new Validate())
            .Then(new Deduplicate(ledger))
            .Then(new ConvertCurrency())
            .Then(new CheckFraud(options.ScoringCost), options.FraudParallelism, options.Ordered);

    /// <summary>A NEW pipeline per import: Deduplicate has a state specific to each file.</summary>
    public static Pipeline<string, ExecutedTransfer> Execute(Ledger ledger, ImportOptions options)
    {
        var checks = Checks(ledger, options);
        if (options.FailureAtReference is { } reference)
            checks = checks.Then(new SimulatedFailure(reference));
        return checks.Then(new Execute(ledger)).Build();
    }

    public static Pipeline<string, Transfer> DryRun(Ledger ledger, ImportOptions options) => Checks(ledger, options).Build();

    /// <summary>The source: the lines of a file, read as they come (the header and the empty lines are skipped).</summary>
    public static async IAsyncEnumerable<string> Lines(TextReader reader, [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        while (await reader.ReadLineAsync(cancellation) is { } line)
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("reference;", StringComparison.OrdinalIgnoreCase))
                yield return line;
    }

    /// <summary>
    /// A big demonstration file, GENERATED on the fly (never entirely in memory), with a few faulty lines of each kind.
    /// </summary>
    public static async IAsyncEnumerable<string> DemoFile(int lines, IReadOnlyList<string> accounts, int seed = 42)
    {
        var random = new Random(seed);
        for (var i = 1; i <= lines; i++)
        {
            var index = random.Next(accounts.Count);
            var payer = accounts[index];
            var payee = accounts[(index + 1 + random.Next(accounts.Count - 1)) % accounts.Count];  // never itself
            var amount = (random.Next(100, 20_000) / 100m).ToString(CultureInfo.InvariantCulture);
            var line = (i % 100) switch
            {
                17 => $"BULK-{i};{payer};{payee}",                                    // malformed
                33 => $"BULK-{i};FR0000000000000000000000000;{payee};{amount};EUR",  // invalid IBAN
                51 => $"BULK-{i - 1};{payer};{payee};{amount};EUR",               // duplicate
                67 => $"BULK-{i};{payer};{payee};{amount};JPY",                   // unknown currency
                89 => $"BULK-{i};{payer};{payee};25000;EUR",                       // ceiling
                _ => $"BULK-{i};{payer};{payee};{amount};{(i % 7 == 0 ? "USD" : "EUR")}"
            };
            yield return line;
            if (i % 1_000 == 0)
                await Task.Yield();
        }
    }

    /// <summary>A demonstration bank: <paramref name="count"/> well-funded accounts.</summary>
    public static (Ledger Ledger, IReadOnlyList<string> Accounts) Sandbox(int count = 1_000)
    {
        var ledger = new Ledger();
        var accounts = Enumerable.Range(1, count).Select(i => Iban.Make($"30006{i:D18}")).ToList();
        foreach (var (iban, i) in accounts.Select((c, i) => (c, i)))
            ledger.Open(iban, $"Customer {i + 1}", 1_000_000m);
        return (ledger, accounts);
    }
}
