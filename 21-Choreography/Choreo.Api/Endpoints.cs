using Choreo.Bus;
using Choreo.Services;
using Choreo.Services.Contracts;

namespace Choreo.Api;

internal static class Endpoints
{
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);
    internal record DecisionRequest(bool Approved);
    internal record ChaosRequest(bool? ExchangeDown, int? NotificationFailures);

    public static void MapTransfers(this IEndpointRouteBuilder app)
    {
        // Requesting a transfer means publishing ONE event. The response is 202: the rest will happen without us.
        // ?wait=true waits for the end (according to Tracking), for the convenience of the demo.
        app.MapPost("/transfers", async (TransferRequest r, bool? wait, Bank bank) =>
        {
            var id = bank.Request(r.Source, r.Target, r.Amount);
            if (wait != true)
                return Results.Accepted($"/transfers/{id}", new { id });
            return Results.Ok(await bank.Wait(id));
        });

        app.MapGet("/transfers", (Bank bank) => Results.Ok(bank.Tracking.All()));

        app.MapGet("/transfers/stuck", (int? delaySeconds, Bank bank) =>
            Results.Ok(bank.Tracking.Stuck(bank.Clock.GetUtcNow(), TimeSpan.FromSeconds(delaySeconds ?? 10))));

        // The state according to Tracking, and the complete story: each event, who published it, and in reaction to what.
        app.MapGet("/transfers/{id}", (string id, Bank bank) =>
        {
            var events = bank.Bus.Log(id);
            if (events.Count == 0)
                return Results.NotFound();
            return Results.Ok(new { state = bank.Tracking.State(id), history = History(events) });
        });
    }

    public static void MapServices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", (Guid id, Bank bank) =>
            bank.Accounts.Get(id) is { } account ? Results.Ok(account) : Results.NotFound());

        app.MapGet("/compliance/pending", (Bank bank) => Results.Ok(bank.Compliance.Pending()));

        // An event coming from a human: the analyst's decision.
        app.MapPost("/compliance/{id}/decision", (string id, DecisionRequest r, Bank bank) =>
            bank.Compliance.Decide(id, r.Approved) ? Results.NoContent()
                : Results.NotFound(new { error = "No decision expected for this transfer (unknown, already decided, or abandoned)." }));

        app.MapGet("/notifications", (Bank bank) => Results.Ok(bank.Notifications.Sent()));

        app.MapGet("/loyalty/{id:guid}", (Guid id, Bank bank) =>
            bank.Loyalty is { } loyalty ? Results.Ok(new { account = id, points = loyalty.Points(id) }) : Results.NotFound());

        app.MapPost("/chaos", (ChaosRequest r, Bank bank) =>
        {
            if (r.ExchangeDown is { } down)
                bank.Exchange.Down = down;
            if (r.NotificationFailures is { } failures)
                bank.Notifications.Sabotage(failures);
            return Results.Ok(new { exchangeDown = bank.Exchange.Down });
        });
    }

    public static void MapBus(this IEndpointRouteBuilder app)
    {
        var bus = app.MapGroup("/bus");

        // Who listens to what: the only "map" of the process, and it only exists at run time.
        bus.MapGet("/", (Bank bank) => Results.Ok(bank.Bus.Subscribers()));

        bus.MapGet("/dead-letters", (Bank bank) => Results.Ok(bank.Bus.DeadLetters().Select(l => new
        {
            l.Subscriber, l.Attempts, l.Error, @event = l.Event.Type, transfer = l.Event.Correlation
        })));

        bus.MapGet("/log", (int? last, Bank bank) =>
            Results.Ok(bank.Bus.Log().TakeLast(last ?? 50).Select(e => new { e.Date, e.Type, e.Emitter, transfer = e.Correlation })));
    }

    /// <summary>The events of a transfer, each one indented under the one that caused it: the chain of reactions.</summary>
    private static IEnumerable<object> History(IReadOnlyList<Event> events)
    {
        var depths = new Dictionary<Guid, int>();
        var types = events.ToDictionary(e => e.Id, e => e.Type);
        foreach (var e in events)
        {
            var depth = e.Cause is { } cause && depths.TryGetValue(cause, out var p) ? p + 1 : 0;
            depths[e.Id] = depth;
            yield return new
            {
                @event = $"{new string(' ', 2 * depth)}{e.Type}",
                e.Emitter,
                inReactionTo = e.Cause is { } c ? types.GetValueOrDefault(c) : null,
                e.Date,
                reason = e switch
                {
                    FundsRefused f => f.Reason, ComplianceRefused f => f.Reason, ExchangeFailed f => f.Reason,
                    FundsReleased f => f.Reason, TransferAbandoned f => f.Reason, CustomerNotified f => f.Message, _ => null
                }
            };
        }
    }
}
