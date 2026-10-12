using System.Text.Json;
using Es.Domain;
using Es.Projections;
using Es.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Es.Api;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

internal static class Endpoints
{
    internal record OpeningRequest(string Owner, decimal InitialDeposit);
    internal record AmountRequest(decimal Amount);
    internal record WithdrawalRequest(decimal Amount, string? Reason);
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);

    // ─── Commands: load (replay) → decide → append the new events ───

    public static void MapCommands(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts", async (OpeningRequest r, AccountRepository repository, TimeProvider clock) =>
        {
            var account = Account.Open(Guid.NewGuid(), r.Owner, r.InitialDeposit, clock.GetUtcNow().UtcDateTime);
            await repository.Save(account);
            return Results.Created($"/accounts/{account.Id}", new { account.Id, account.Version });
        });

        app.MapPost("/accounts/{id:guid}/deposit", (Guid id, AmountRequest r, [FromHeader(Name = "If-Match")] int? version,
                AccountRepository repository, TimeProvider clock) =>
            Modify(id, version, repository, c => c.Deposit(r.Amount, clock.GetUtcNow().UtcDateTime)));

        app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, WithdrawalRequest r, [FromHeader(Name = "If-Match")] int? version,
                AccountRepository repository, TimeProvider clock) =>
            Modify(id, version, repository, c => c.Withdraw(r.Amount, r.Reason ?? "Counter withdrawal", clock.GetUtcNow().UtcDateTime)));

        app.MapPost("/accounts/{id:guid}/closure", (Guid id, [FromHeader(Name = "If-Match")] int? version,
                AccountRepository repository, TimeProvider clock) =>
            Modify(id, version, repository, c => c.Close(clock.GetUtcNow().UtcDateTime)));

        app.MapPost("/transfers", async (TransferRequest r, AccountRepository repository, TimeProvider clock) =>
        {
            if (r.Source == r.Target)
                throw new BusinessRuleException("Cannot transfer to the same account.");

            var (source, target) = (await repository.Load(r.Source), await repository.Load(r.Target));
            var (transferId, now) = (Guid.NewGuid(), clock.GetUtcNow().UtcDateTime);
            source.SendTransfer(transferId, target.Id, r.Amount, now);
            target.ReceiveTransfer(transferId, source.Id, r.Amount, now);

            // Two streams changed, written all or nothing (if one moved in the meantime: 409, nothing is written).
            await repository.Save(source, target);
            return Results.Ok(new { transferId, sourceBalance = source.Balance, targetBalance = target.Balance });
        });
    }

    /// <param name="expectedVersion">
    /// Optional If-Match header: "I am changing the account as I saw it at this version".
    /// If it changed since, the change is refused (409) instead of overwriting a decision made on stale data.
    /// </param>
    private static async Task<IResult> Modify(Guid id, int? expectedVersion, AccountRepository repository, Action<Account> decision)
    {
        var account = await repository.Load(id);
        if (expectedVersion is not null && expectedVersion != account.Version)
            throw new ConcurrencyConflictException(id, expectedVersion.Value, account.Version);

        decision(account);
        await repository.Save(account);
        return Results.Ok(new { account.Id, account.Balance, account.Version });
    }

    // ─── Reads ───

    public static void MapReads(this IEndpointRouteBuilder app)
    {
        // Current view, served by a projection.
        app.MapGet("/accounts/{id:guid}", async (Guid id, ProjectionEngine engine, StatementsProjection statements) =>
            await engine.Read(() => statements.Get(id)) is { } statement ? Results.Ok(statement) : Results.NotFound());

        // Audit: the complete and immutable history of the account.
        app.MapGet("/accounts/{id:guid}/events", async (Guid id, IEventStore store) =>
            (await store.ReadStream(id)).Select(e => new
            {
                e.Version,
                e.Position,
                e.Type,
                e.Date,
                Data = JsonSerializer.Deserialize<JsonElement>(e.Data)
            }));

        // Time travel: the state at a past version or date, recomputed by replaying the events.
        app.MapGet("/accounts/{id:guid}/state", async (Guid id, int? version, DateTime? date, AccountRepository repository) =>
        {
            var account = version is not null ? await repository.LoadAtVersion(id, version.Value)
                : date is not null ? await repository.LoadAtDate(id, date.Value.ToUniversalTime())
                : await repository.Load(id);
            return Results.Ok(new { account.Id, account.Owner, account.Balance, account.Closed, account.Version });
        });

        app.MapGet("/statistics/largest-withdrawals", (ProjectionEngine engine, LargestWithdrawalsProjection withdrawals) =>
            engine.Read(() => withdrawals.Top(5)));

        // The projections are disposable: they are cleared and the whole store is replayed.
        app.MapPost("/admin/projections/rebuild", async (ProjectionEngine engine) =>
            Results.Ok(new { eventsReplayed = await engine.Rebuild() }));

        app.MapGet("/admin/store", async (IEventStore store, ProjectionEngine engine) =>
        {
            var all = await store.ReadAll();
            return Results.Ok(new
            {
                eventCount = all.Count,
                streamCount = all.Select(e => e.Stream).Distinct().Count(),
                byType = all.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count()),
                projectionPosition = engine.Position
            });
        });
    }
}

/// <summary>Feeds the new events to the projections, continuously.</summary>
internal sealed class ProjectionSubscription(ProjectionEngine engine) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            await engine.CatchUp();
            await Task.Delay(TimeSpan.FromMilliseconds(100), stopping);
        }
    }
}
