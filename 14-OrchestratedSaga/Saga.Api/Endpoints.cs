using Saga.Orchestrator;
using Saga.Participants;

namespace Saga.Api;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
}

/// <summary>
/// Runs the engine continuously. It can be stopped and restarted to see that the sagas
/// resume exactly where they were (their state is in the store, not in the engine).
/// </summary>
public sealed class OrchestratorLoop(SagaEngine engine, ILogger<OrchestratorLoop> logger) : BackgroundService
{
    public bool Paused { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            if (!Paused)
            {
                try
                {
                    await engine.Process();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Unexpected engine error");
                }
            }
            await Task.Delay(100, stopping);
        }
    }
}

internal static class Endpoints
{
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);
    internal record DecisionRequest(bool Approved, string? Comment);
    internal record ChaosRequest(int? ExchangeFailuresLeft, bool? ExchangeDown, bool? NotificationsDown);

    public static void MapSagas(this IEndpointRouteBuilder app)
    {
        // ─── Start and track a saga ───
        app.MapPost("/transfers", (TransferRequest r, SagaEngine engine) =>
        {
            if (r.Amount <= 0 || r.Source == r.Target)
                return Results.BadRequest(new { error = "Positive amount and different accounts required." });
            var saga = engine.Start(r.Source, r.Target, r.Amount);
            return Results.Accepted($"/sagas/{saga.Id}", new { sagaId = saga.Id });
        });

        app.MapGet("/sagas/{id:guid}", (Guid id, SagaStore store, SagaEngine engine) =>
            store.Load(id) is { } saga ? Results.Ok(View(saga, engine)) : Results.NotFound());

        app.MapGet("/sagas", (SagaStore store) => store.All()
            .Select(s => new { s.Id, Status = s.Status.ToString(), s.Amount, s.Reason, Entries = s.Log.Count }));

        // ─── External event: the decision of a compliance analyst ───
        app.MapPost("/sagas/{id:guid}/compliance", async (Guid id, DecisionRequest d, SagaEngine engine) =>
            await engine.Signal(id, d.Approved, d.Comment)
                ? Results.NoContent()
                : Results.Conflict(new { error = "This saga is not waiting for a decision." }));

        // ─── The participants (in reality, other services) ───
        app.MapGet("/accounts", (AccountsService accounts) => accounts.All());
        app.MapGet("/notifications", (NotificationsService notifications) => notifications.Sent);

        // ─── Demo: cause failures, stop / restart the orchestrator ───
        app.MapPut("/chaos", (ChaosRequest r, Chaos chaos) =>
        {
            chaos.ExchangeFailuresLeft = r.ExchangeFailuresLeft ?? chaos.ExchangeFailuresLeft;
            chaos.ExchangeDown = r.ExchangeDown ?? chaos.ExchangeDown;
            chaos.NotificationsDown = r.NotificationsDown ?? chaos.NotificationsDown;
            return Results.Ok(chaos);
        });
        app.MapPost("/admin/orchestrator/stop", (OrchestratorLoop loop) => { loop.Paused = true; return Results.NoContent(); });
        app.MapPost("/admin/orchestrator/restart", (OrchestratorLoop loop) => { loop.Paused = false; return Results.NoContent(); });
    }

    /// <summary>Tracking view: the state of each step, deduced from the saved state of the saga.</summary>
    private static object View(SagaState saga, SagaEngine engine) => new
    {
        saga.Id,
        Status = saga.Status.ToString(),
        saga.Amount,
        saga.CreditedAmount,
        saga.TargetCurrency,
        saga.Reason,
        saga.Deadline,
        Steps = engine.StepNames.Select((name, i) => new
        {
            name,
            state = saga.CompletedSteps.Contains(name) ? "done"
                : saga.Status is SagaStatus.Compensating or SagaStatus.Cancelled && i < saga.CurrentStep ? "undone"
                : i == saga.CurrentStep && !saga.IsFinished ? saga.Status.ToString()
                : "—"
        }),
        Log = saga.Log.Select(j => $"{j.Date:HH:mm:ss.fff} {j.Message}")
    };
}
