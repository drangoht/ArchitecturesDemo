using Pipeline.Bank;
using Pipeline.Core;

namespace Pipeline.Tests;

/// <summary>The bank filters, assembled as in the API, on the sample file and on a big generated file.</summary>
public class ImportTests
{
    private const string Alice = DemoData.Alice, Bob = DemoData.Bob;

    private readonly Ledger _ledger = DemoData.Ledger();

    /// <summary>The API's sample file (Transfers-sample.csv), found from the tests' output folder.</summary>
    private static string[] SampleFile()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(folder.FullName, "Pipeline.Api", "Transfers-sample.csv")))
            folder = folder.Parent!;
        return File.ReadAllLines(Path.Combine(folder.FullName, "Pipeline.Api", "Transfers-sample.csv"));
    }

    private static async IAsyncEnumerable<string> Source(IEnumerable<string> lines)
    {
        using var reader = new StringReader(string.Join('\n', lines));
        await foreach (var line in Imports.Lines(reader))
            yield return line;
    }

    private Task<Report> Import(ImportOptions? options = null) =>
        Imports.Execute(_ledger, options ?? new ImportOptions(FraudParallelism: 4)).Run(Source(SampleFile()));

    private decimal Balance(string iban) => _ledger.Get(iban)!.Balance;

    [Fact]
    public async Task The_sample_file_gives_three_transfers_and_one_rejection_per_reason()
    {
        var report = await Import();

        Assert.Equal((RunStatus.Completed, 11L, 3L, 8L), (report.Status, report.Lines, report.Outputs, report.Rejections));
        Assert.All(report.RejectionsByReason.Values, n => Assert.Equal(1, n));
        Assert.Equal(
            [(3L, "Parse"), (4, "Validate"), (5, "Validate"), (6, "Deduplicate"), (7, "Convert currency"), (8, "Check fraud"),
             (9, "Check fraud"), (10, "Execute")],
            report.FirstRejections.Select(r => (r.Line, r.Filter)));
        Assert.Equal((842.80m, 657.20m), (Balance(Alice), Balance(Bob)));  // -120 +46 (50 USD) -83.20 (80 CHF)
    }

    [Fact]
    public async Task Replaying_a_file_executes_nothing_twice()
    {
        await Import();

        var replay = await Import();

        Assert.Equal(4, replay.RejectionsByReason["duplicate"]);  // VIR-001, 002, 010 already executed, and VIR-001 twice in the file
        // Discarded early, by a cheap filter: they reach neither the fraud check nor the execution.
        Assert.All(replay.FirstRejections.Where(r => r.Reason == "duplicate"), r => Assert.Equal("Deduplicate", r.Filter));
        Assert.Equal(1, replay.Outputs);                     // only VIR-009 goes through: Bob now has enough to pay
        Assert.Equal((1_442.80m, 57.20m), (Balance(Alice), Balance(Bob)));
    }

    [Fact]
    public async Task A_dry_run_import_tells_what_would_go_through_without_modifying_anything()
    {
        var outputs = new List<Transfer>();

        var report = await Imports.DryRun(_ledger, new ImportOptions()).Run(Source(SampleFile()), outputs.Add);

        Assert.Equal(["VIR-001", "VIR-002", "VIR-009", "VIR-010"], outputs.Select(v => v.Reference));
        Assert.DoesNotContain("Execute", report.Stages.Select(e => e.Name));
        Assert.Equal((1_000m, 500m, 0), (Balance(Alice), Balance(Bob), _ledger.ExecutedCount));
    }

    [Fact]
    public async Task After_a_failure_in_the_middle_of_an_import_the_replay_finishes_without_executing_anything_twice()
    {
        var failure = await Import(new ImportOptions(FailureAtReference: "VIR-010"));

        Assert.Equal(RunStatus.Failed, failure.Status);
        Assert.Equal("Simulated failure while processing VIR-010.", failure.Error);
        Assert.InRange(_ledger.ExecutedCount, 0, 2);  // depending on what had reached the execution before the stop

        var resumption = await Import();

        Assert.Equal(RunStatus.Completed, resumption.Status);
        Assert.Equal(3, _ledger.ExecutedCount);
        Assert.Equal((842.80m, 657.20m), (Balance(Alice), Balance(Bob)));  // exactly as without a failure
    }

    [Fact]
    public async Task The_file_order_matters_and_the_ordered_parallel_stage_respects_it()
    {
        // Bob can only pay line 2 after having received line 1. Line 1 is the slowest to check.
        string[] file = [$"L1;{Alice};{Bob};600;EUR", $"L2;{Bob};{Alice};1000;EUR"];
        Pipeline<string, ExecutedTransfer> Assemble(Ledger ledger, bool ordered) => Core.Pipeline.From<string>()
            .Then(new Parse())
            .Then(new ConvertCurrency())
            .Then(new Slow<Transfer>("Slow check", v => v.Reference == "L1" ? 100 : 0), parallelism: 4, ordered)
            .Then(new Execute(ledger))
            .Build();

        var ordered = await Assemble(_ledger, ordered: true).Run(Source(file));
        var otherLedger = DemoData.Ledger();
        var unordered = await Assemble(otherLedger, ordered: false).Run(Source(file));

        Assert.Equal(2, ordered.Outputs);
        Assert.Equal(1, unordered.Outputs);
        Assert.Equal("insufficient balance", unordered.FirstRejections.Single().Reason);
    }

    [Fact]
    public async Task A_big_file_flows_through_with_bounded_memory()
    {
        var (ledger, accounts) = Imports.Sandbox(200);
        var total = ledger.Balances().Values.Sum();

        var report = await Imports.Execute(ledger, new ImportOptions(FraudParallelism: 4)).Run(Imports.DemoFile(20_000, accounts));

        Assert.Equal((RunStatus.Completed, 20_000L), (report.Status, report.Lines));
        Assert.Equal(report.Lines, report.Outputs + report.Rejections);
        foreach (var reason in new[] { "malformed line", "invalid IBAN", "duplicate", "ceiling exceeded" })
            Assert.Equal(200, report.RejectionsByReason[reason]);
        Assert.InRange(report.MaxInFlight, 1, 1_000);  // 20,000 lines, never more than a few hundred in memory
        Assert.Equal(total, ledger.Balances().Values.Sum());
    }

    [Fact]
    public async Task Parallelizing_the_fraud_check_does_not_change_the_result()
    {
        var (single, accounts) = Imports.Sandbox(50);
        var (eight, _) = Imports.Sandbox(50);

        await Imports.Execute(single, new ImportOptions(FraudParallelism: 1, ScoringCost: 1)).Run(Imports.DemoFile(5_000, accounts));
        await Imports.Execute(eight, new ImportOptions(FraudParallelism: 8, ScoringCost: 1)).Run(Imports.DemoFile(5_000, accounts));

        Assert.Equal(single.Balances(), eight.Balances());
    }

    [Fact]
    public async Task Execute_refuses_a_transfer_that_did_not_go_through_the_conversion()
    {
        // The filters ignore each other, but the pipeline has a logical order: Execute assumes Convert currency came before.
        var report = await Core.Pipeline.From<string>().Then(new Parse()).Then(new Execute(_ledger)).Build()
            .Run(Source([$"X-1;{Alice};{Bob};100;EUR"]));

        Assert.Equal("amount not converted", report.FirstRejections.Single().Reason);
        Assert.Equal(1_000m, Balance(Alice));
    }

    [Fact]
    public void The_ledger_never_executes_the_same_reference_twice()
    {
        // Last line of defence: two SIMULTANEOUS imports of the same file both go through Deduplicate before one has executed.
        var transfer = new Transfer("R-1", Alice, Bob, 100m, "EUR", 100m);

        Assert.Null(_ledger.Transfer(transfer, out _));
        Assert.Equal("duplicate", _ledger.Transfer(transfer, out _));
        Assert.Equal(900m, Balance(Alice));
    }

    [Theory]
    [InlineData("FR7630006000011234567890189", true)]
    [InlineData("FR7630006000011234567890188", false)]
    [InlineData("FR76 3000", false)]
    public void The_check_digits_of_an_iban_are_verified(string iban, bool valid) => Assert.Equal(valid, Iban.IsValid(iban));
}
