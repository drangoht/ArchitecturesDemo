using System.Diagnostics;
using System.Text.RegularExpressions;
using Actors.Bank;
using Actors.Runtime;

namespace Actors.Api;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
}

internal static partial class Endpoints
{
    internal record OpeningRequest(string Owner, decimal InitialDeposit);
    internal record AmountRequest(decimal Amount);
    internal record TransferRequest(string? Id, Guid Source, Guid Target, decimal Amount);
    internal record SimultaneousWithdrawalsRequest(Guid Account, int Count, decimal Amount);
    internal record CrossedTransfersRequest(int Count, decimal Amount);

    [GeneratedRegex("^[A-Za-z0-9-]{1,40}$")]
    private static partial Regex ValidId();

    /// <summary>
    /// Each route translates the HTTP request into ONE message for ONE actor, and its response into an HTTP code.
    /// The API neither reads nor writes any state: it only talks to the actors.
    /// </summary>
    public static void MapBank(this IEndpointRouteBuilder app)
    {
        app.MapPost("/accounts", async (OpeningRequest r, ActorSystem system) =>
        {
            var id = Guid.NewGuid();
            var response = await system.Account(id).Ask(new OpenAccount(r.Owner, r.InitialDeposit));
            return response is AccountDto account ? Results.Created($"/accounts/{id}", account) : Http(response);
        });

        app.MapGet("/accounts/{id:guid}", async (Guid id, ActorSystem system) =>
            Http(await system.Account(id).Ask(new GetAccount())));

        app.MapGet("/accounts/{id:guid}/operations", async (Guid id, ActorSystem system) =>
            Http(await system.Account(id).Ask(new ListOperations())));

        app.MapPost("/accounts/{id:guid}/deposit", async (Guid id, AmountRequest r, ActorSystem system) =>
            Http(await system.Account(id).Ask(new Deposit(r.Amount))));

        app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, AmountRequest r, ActorSystem system) =>
            Http(await system.Account(id).Ask(new Withdraw(r.Amount))));

        // The transfer identifier, if provided by the client, makes the request replayable without risk.
        app.MapPost("/transfers", async (TransferRequest r, ActorSystem system) =>
        {
            var id = r.Id ?? $"v-{Guid.NewGuid():N}"[..14];
            if (!ValidId().IsMatch(id))
                return Results.BadRequest(new { error = "Transfer identifier: 1 to 40 letters, digits or hyphens." });
            return Http(await system.Transfer(id).Ask(new ExecuteTransfer(r.Source, r.Target, r.Amount)));
        });

        app.MapGet("/transfers/{id}", async (string id, ActorSystem system) =>
            ValidId().IsMatch(id) ? Http(await system.Transfer(id).Ask(new GetTransfer())) : Results.NotFound());
    }

    /// <summary>Observe the runtime and cause failures.</summary>
    public static void MapSystem(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/system");

        group.MapGet("/", (ActorSystem system) => Results.Ok(system.State()));

        group.MapGet("/log", (ActorSystem system) => Results.Ok(system.Log()));

        // The durable storage: a passivated actor is no longer in memory, but its row is still there.
        group.MapGet("/storage", (string? type, StateStore store) =>
            Results.Ok(store.Paths().Where(c => type is null || c.StartsWith($"{type}/"))));

        group.MapGet("/failures", (Traps traps) => Results.Ok(traps.Armed()));

        group.MapPost("/failures", (Trap trap, Traps traps) =>
        {
            traps.Arm(trap);
            return Results.Ok(traps.Armed());
        });

        group.MapDelete("/failures", (Traps traps) =>
        {
            traps.DisarmAll();
            return Results.NoContent();
        });

        // Without waiting for the automatic passivation: put to sleep any actor idle for N seconds (0 = all the idle ones).
        group.MapPost("/passivation", (int? inactivitySeconds, ActorSystem system) =>
            Results.Ok(new { passivated = system.Passivate(TimeSpan.FromSeconds(inactivitySeconds ?? 0)) }));
    }

    /// <summary>The concurrency demonstrations: many messages at the same time, no lock in the business code.</summary>
    public static void MapDemonstrations(this IEndpointRouteBuilder app)
    {
        app.MapPost("/demo/simultaneous-withdrawals", async (SimultaneousWithdrawalsRequest r, ActorSystem system) =>
        {
            if (r.Count is < 1 or > 10_000)
                return Results.BadRequest(new { error = "Count: from 1 to 10,000." });
            var account = system.Account(r.Account);
            if (await account.Ask(new GetAccount()) is not AccountDto before)
                return Results.NotFound();

            var stopwatch = Stopwatch.StartNew();
            var responses = await Task.WhenAll(Enumerable.Range(0, r.Count)
                .Select(_ => Task.Run(() => account.Ask(new Withdraw(r.Amount)))));
            var accepted = responses.OfType<AccountDto>().ToList();

            return Results.Ok(new
            {
                balanceBefore = before.Balance,
                requests = r.Count,
                accepted = accepted.Count,
                refused = responses.OfType<Refusal>().Count(),
                finalBalance = ((AccountDto)(await account.Ask(new GetAccount()))!).Balance,
                // Each accepted response carries the balance right after ITS withdrawal: never negative.
                minimumObservedBalance = accepted.Count > 0 ? accepted.Min(c => c.Balance) : before.Balance,
                durationMs = stopwatch.ElapsedMilliseconds
            });
        });

        app.MapPost("/demo/crossed-transfers", async (CrossedTransfersRequest r, ActorSystem system) =>
        {
            if (r.Count is < 1 or > 5_000)
                return Results.BadRequest(new { error = "Count: from 1 to 5,000 in each direction." });

            var before = await Total(system);
            var stopwatch = Stopwatch.StartNew();
            var series = Guid.NewGuid().ToString("N")[..6];
            var transfers = Enumerable.Range(0, r.Count).SelectMany(i => new[]
            {
                (Id: $"x{series}-ab-{i}", Source: DemoData.Alice, Target: DemoData.Bob),
                (Id: $"x{series}-ba-{i}", Source: DemoData.Bob, Target: DemoData.Alice)
            });
            var responses = await Task.WhenAll(transfers.Select(v => Task.Run(() =>
                system.Transfer(v.Id).Ask(new ExecuteTransfer(v.Source, v.Target, r.Amount)))));

            return Results.Ok(new
            {
                transfers = responses.Length,
                completed = responses.OfType<TransferDto>().Count(v => v.Status == TransferStatus.Completed),
                refused = responses.OfType<TransferDto>().Count(v => v.Status == TransferStatus.Refused),
                totalBefore = before,
                totalAfter = await Total(system),  // money is neither created nor lost
                durationMs = stopwatch.ElapsedMilliseconds
            });
        });
    }

    private static async Task<decimal> Total(ActorSystem system)
    {
        var alice = (AccountDto)(await system.Account(DemoData.Alice).Ask(new GetAccount()))!;
        var bob = (AccountDto)(await system.Account(DemoData.Bob).Ask(new GetAccount()))!;
        return alice.Balance + bob.Balance;
    }

    /// <summary>An actor's response → HTTP.</summary>
    private static IResult Http(object? response) => response switch
    {
        Refusal { NotFound: true } r => Results.NotFound(new { error = r.Reason }),
        Refusal r => Results.BadRequest(new { error = r.Reason }),
        _ => Results.Ok(response)
    };
}
