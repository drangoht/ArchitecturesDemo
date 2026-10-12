using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using Pipeline.Bank;
using Pipeline.Core;

namespace Pipeline.Api;

public sealed class ApiOptions
{
    /// <summary>Copies of the fraud filter working at the same time, by default.</summary>
    public int FraudParallelism { get; set; } = 4;

    /// <summary>Simulated cost of the fraud score (in thousands of hashing rounds per transfer).</summary>
    public int ScoringCost { get; set; } = 50;
}

public sealed record RecordedImport(int Id, DateTimeOffset Date, bool DryRun, Report Report);

/// <summary>The reports of the imports already done.</summary>
public sealed class History
{
    private readonly ConcurrentDictionary<int, RecordedImport> _imports = new();
    private int _last;

    public RecordedImport Add(bool dryRun, Report report)
    {
        var import = new RecordedImport(Interlocked.Increment(ref _last), DateTimeOffset.Now, dryRun, report);
        _imports[import.Id] = import;
        return import;
    }

    public RecordedImport? Find(int id) => _imports.GetValueOrDefault(id);

    public IEnumerable<RecordedImport> All() => _imports.Values.OrderBy(i => i.Id);
}

internal static class Endpoints
{
    internal record BulkImportRequest(int Lines = 100_000, int FraudParallelism = 1, bool Ordered = true, int? ScoringCost = null);

    public static void MapAccounts(this IEndpointRouteBuilder app) =>
        app.MapGet("/accounts/{iban}", (string iban, Ledger ledger) =>
            ledger.Get(iban.ToUpperInvariant()) is { } account ? Results.Ok(account) : Results.NotFound());

    public static void MapImports(this IEndpointRouteBuilder app)
    {
        // The request body is the CSV file, read line by line WHILE it arrives: it is never loaded entirely.
        app.MapPost("/imports", async (HttpRequest request, bool? dryRun, int? parallelism, string? failureAtReference,
            Ledger ledger, History history, IOptions<ApiOptions> configuration, CancellationToken cancellation) =>
        {
            var options = new ImportOptions(parallelism ?? configuration.Value.FraudParallelism, Ordered: true,
                configuration.Value.ScoringCost, failureAtReference);
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var lines = Imports.Lines(reader, cancellation);

            var report = dryRun == true
                ? await Imports.DryRun(ledger, options).Run(lines, cancellation: cancellation)
                : await Imports.Execute(ledger, options).Run(lines, cancellation: cancellation);

            var import = history.Add(dryRun == true, report);
            return Results.Json(import, statusCode: report.Status == RunStatus.Completed ? 200 : 500);
        });

        app.MapGet("/imports", (History history) => Results.Ok(history.All().Select(i => new
        {
            i.Id, i.Date, i.DryRun, i.Report.Status, i.Report.Lines, i.Report.Outputs, i.Report.Rejections
        })));

        app.MapGet("/imports/{id:int}", (int id, History history) =>
            history.Find(id) is { } import ? Results.Ok(import) : Results.NotFound());
    }

    public static void MapDemonstrations(this IEndpointRouteBuilder app) =>
        // A file of N lines generated on the fly, imported into a demonstration bank of 1,000 accounts.
        // Compare fraudParallelism = 1 and 8: the bottleneck, the throughput, and the memory (maxInFlight) which does not depend on N.
        app.MapPost("/demo/bulk-import", async (BulkImportRequest r, IOptions<ApiOptions> configuration, CancellationToken cancellation) =>
        {
            if (r.Lines is < 1 or > 2_000_000 || r.FraudParallelism is < 1 or > 64)
                return Results.BadRequest(new { error = "Lines: from 1 to 2,000,000; parallelism: from 1 to 64." });
            var (ledger, accounts) = Imports.Sandbox();
            var options = new ImportOptions(r.FraudParallelism, r.Ordered, r.ScoringCost ?? configuration.Value.ScoringCost);

            var report = await Imports.Execute(ledger, options).Run(Imports.DemoFile(r.Lines, accounts), cancellation: cancellation);

            return Results.Ok(new
            {
                report.Status, report.Lines, executed = report.Outputs, report.Rejections, report.RejectionsByReason,
                report.DurationMs, report.ThroughputPerSecond, report.MaxInFlight, report.Bottleneck, report.Stages
            });
        });
}
