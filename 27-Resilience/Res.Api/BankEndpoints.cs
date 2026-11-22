using Res.Bank;
using Res.FraudSimulator;
using Res.Resilience;

namespace Res.Api;

public static class BankEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/accounts/{id:guid}", (Guid id, Ledger ledger) => Run(() => Task.FromResult(Results.Ok(ledger.Get(id)))));

        app.MapPost("/transfers", (TransferRequest request, TransferService transfers, HttpContext http, CancellationToken ct) => Run(async () =>
        {
            try
            {
                return Results.Ok(await transfers.Transfer(request.Source, request.Target, request.Amount, ct));
            }
            catch (FraudCheckUnavailableException e)
            {
                http.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(e.RetryAfter.TotalSeconds)).ToString("0");
                return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }));

        // What the strategies did, and what the partner received.
        app.MapGet("/resilience", (CircuitBreaker breaker, Bulkhead bulkhead, ResilienceEvents events, FraudPartner partner) =>
            Results.Ok(new
            {
                Circuit = breaker.State.ToString(),
                bulkhead.InProgress,
                bulkhead.Queued,
                PartnerMode = partner.Mode.ToString(),
                PartnerCallsReceived = partner.CallsReceived,
                Events = events.Latest(30).Select(e => $"{e.Strategy}: {e.Detail}")
            }));

        // The chaos switch of the simulated partner.
        app.MapPost("/partner/mode", (PartnerModeRequest request, FraudPartner partner) =>
        {
            if (!Enum.TryParse<PartnerMode>(request.Mode, ignoreCase: true, out var mode))
                return Results.BadRequest(new { error = $"Unknown mode: {request.Mode} (healthy, flaky, slow, down)." });
            partner.Set(mode, request.Failures ?? 2);
            return Results.Ok(new { Mode = mode.ToString() });
        });
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NotFoundException e)
        {
            return Results.NotFound(new { error = e.Message });
        }
        catch (BusinessRuleException e)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }
}

public record TransferRequest(Guid Source, Guid Target, decimal Amount);

public record PartnerModeRequest(string Mode, int? Failures);
