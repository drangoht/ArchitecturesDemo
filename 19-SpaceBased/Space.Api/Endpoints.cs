using System.Diagnostics;
using Space.Bank;
using Space.Grid;

namespace Space.Api;

internal static class Endpoints
{
    internal record OpeningRequest(string Owner, decimal InitialDeposit);
    internal record AmountRequest(decimal Amount);
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);
    internal record RoutingRequest(RoutingMode Mode);
    internal record ConcurrentWithdrawalsRequest(Guid Account, int Count, decimal Amount);
    internal record LoadRequest(int Count);

    /// <summary>
    /// Each route turns the HTTP request into a business request and hands it to the messaging grid, which chooses the unit.
    /// The X-Unit header tells which unit answered.
    /// </summary>
    public static void MapBank(this IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", (Guid id, Platform p, HttpContext h) => Send(p, h, new GetAccount(id)));

        app.MapPost("/accounts", async (OpeningRequest r, Platform p, HttpContext h) =>
        {
            var id = Guid.NewGuid();
            var result = await Send(p, h, new OpenAccount(id, r.Owner, r.InitialDeposit));
            return result is IValueHttpResult { Value: AccountDto account } ? Results.Created($"/accounts/{id}", account) : result;
        });

        app.MapPost("/accounts/{id:guid}/deposit", (Guid id, AmountRequest r, Platform p, HttpContext h) =>
            Send(p, h, new Deposit(id, r.Amount)));

        app.MapPost("/accounts/{id:guid}/withdrawal", (Guid id, AmountRequest r, Platform p, HttpContext h) =>
            Send(p, h, new Withdraw(id, r.Amount)));

        app.MapPost("/transfers", (TransferRequest r, Platform p, HttpContext h) =>
            Send(p, h, new Transfer(r.Source, r.Target, r.Amount)));
    }

    /// <summary>Observe and handle the middleware: units, routing, replication, pump, database.</summary>
    public static void MapGrid(this IEndpointRouteBuilder app)
    {
        var grid = app.MapGroup("/grid");

        grid.MapGet("/", (Platform p) => Results.Ok(p.State()));

        grid.MapGet("/log", (Platform p) => Results.Ok(p.Log()));

        // One piece of data seen by each replica and by the database: identical at rest, different during replication.
        grid.MapGet("/comparison/{id:guid}", (Guid id, Platform p) => Results.Ok(new
        {
            replicas = p.Data.Replicas().OrderBy(r => r.Unit).Select(r => View(r.Unit, r.Read(id.ToString()))),
            database = View("database", p.Database.Read(id.ToString()))
        }));

        grid.MapPost("/routing", (RoutingRequest r, Platform p) =>
        {
            p.Messages.Mode = r.Mode;
            return Results.Ok(new { routing = p.Messages.Mode });
        });

        grid.MapPost("/units", async (Platform p) => Results.Ok(new { unit = (await p.Deployment.Start()).Id }));

        grid.MapDelete("/units/{id}", async (string id, Platform p) =>
        {
            if (Find(p, id) is not { } unit)
                return Results.NotFound();
            await p.Deployment.Stop(unit);
            return Results.NoContent();
        });

        grid.MapPost("/units/{id}/failure", (string id, Platform p) =>
        {
            if (Find(p, id) is not { } unit)
                return Results.NotFound();
            p.Deployment.Kill(unit);
            return Results.NoContent();
        });

        // All the units go down: the in-memory grid disappears. The next request causes a cold start.
        grid.MapPost("/total-failure", (Platform p) =>
        {
            foreach (var unit in p.Messages.Units())
                p.Deployment.Kill(unit);
            return Results.NoContent();
        });

        grid.MapPost("/database/failure", (Platform p) =>
        {
            p.Database.Available = false;
            return Results.NoContent();
        });

        grid.MapPost("/database/restore", (Platform p) =>
        {
            p.Database.Available = true;
            return Results.NoContent();
        });

        grid.MapPost("/evaluate", async (Platform p) => Results.Ok(new { decision = await p.Deployment.Evaluate() }));
    }

    /// <summary>The demonstrations: collisions depending on the routing, elasticity under load.</summary>
    public static void MapDemonstrations(this IEndpointRouteBuilder app)
    {
        // Many simultaneous withdrawals on ONE account. With affinity, a single unit handles them: the account is right.
        // With round robin, several units write the same account at the same time: writes get lost.
        app.MapPost("/demo/concurrent-withdrawals", async (ConcurrentWithdrawalsRequest r, Platform p) =>
        {
            if (r.Count is < 1 or > 5_000)
                return Results.BadRequest(new { error = "Count: from 1 to 5,000." });
            if ((await p.Send(new GetAccount(r.Account))).Response is not AccountDto before)
                return Results.NotFound();
            var lostBefore = p.Data.LostUpdates;

            var results = await Task.WhenAll(Enumerable.Range(0, r.Count)
                .Select(_ => Task.Run(() => p.Send(new Withdraw(r.Account, r.Amount)))));
            await p.WaitForStabilization();

            var accepted = results.Count(x => x.Response is AccountDto);
            var key = r.Account.ToString();
            return Results.Ok(new
            {
                routing = p.Messages.Mode,
                unitsUsed = results.Select(x => x.Unit).Distinct().Order(),
                balanceBefore = before.Balance,
                accepted,
                refused = r.Count - accepted,
                expectedBalance = before.Balance - accepted * r.Amount,
                balanceInGrid = p.Data.Replicas().Select(x => ((Account)x.Read(key)!.Value).Balance).Distinct(),
                balanceInDatabase = ((Account)p.Database.Read(key)!.Value).Balance,
                lostUpdates = p.Data.LostUpdates - lostBefore
            });
        });

        // A flood of account openings (keys all different, hence spread among the units).
        app.MapPost("/demo/load", async (LoadRequest r, Platform p) =>
        {
            if (r.Count is < 1 or > 5_000)
                return Results.BadRequest(new { error = "Count: from 1 to 5,000." });
            var unitsBefore = p.Messages.Units().Count;
            var maxUnits = unitsBefore;
            using var end = new CancellationTokenSource();
            var observation = Task.Run(async () =>
            {
                while (!end.IsCancellationRequested)
                {
                    maxUnits = Math.Max(maxUnits, p.Messages.Units().Count);
                    await Task.Delay(20);
                }
            });

            var stopwatch = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(1, r.Count)
                .Select(i => Task.Run(() => p.Send(new OpenAccount(Guid.NewGuid(), $"Customer {i}", 10m)))));
            stopwatch.Stop();
            await end.CancelAsync();
            await observation;

            return Results.Ok(new
            {
                requests = r.Count,
                durationMs = stopwatch.ElapsedMilliseconds,
                unitsBefore,
                maxUnits,
                unitsNow = p.Messages.Units().Count,
                distribution = p.Messages.Units().Select(u => new { u.Id, u.Processed })
            });
        });
    }

    private static async Task<IResult> Send(Platform platform, HttpContext http, IRequest request)
    {
        var (response, unit) = await platform.Send(request);
        http.Response.Headers["X-Unit"] = unit;
        return response switch
        {
            Refusal { NotFound: true } r => Results.NotFound(new { error = r.Reason }),
            Refusal r => Results.BadRequest(new { error = r.Reason }),
            _ => Results.Ok(response)
        };
    }

    private static ProcessingUnit? Find(Platform p, string id) => p.Messages.Units().FirstOrDefault(u => u.Id == id);

    private static object View(string where, Entry? entry) =>
        new { where, version = entry?.Version.ToString(), balance = (entry?.Value as Account)?.Balance, operations = (entry?.Value as Account)?.Operations.Count };
}
