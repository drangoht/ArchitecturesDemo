namespace Bff.Services;

/// <summary>Simulated failures and slowness, settable through PUT /chaos.</summary>
public sealed class Chaos
{
    public int LatencyMs { get; set; }
    public bool OffersDown { get; set; }
}

internal static class Endpoints
{
    internal record TokenRequest(string User, string Password);
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);
    internal record ChaosRequest(int? LatencyMs, bool? OffersDown);

    private static string User(HttpContext http) => (string)http.Items["user"]!;

    private static IResult Forbidden() =>
        Results.Json(new { error = "This data does not belong to the token's user." }, statusCode: StatusCodes.Status403Forbidden);

    public static void MapServices(this IEndpointRouteBuilder app)
    {
        // ─── Identity ───
        app.MapPost("/identity/token", (TokenRequest d, Bank bank, Tokens tokens) =>
            bank.Users.TryGetValue(d.User, out var u) && u.Password == d.Password
                ? Results.Ok(new { token = tokens.Issue(u.Id), expiresIn = (int)Tokens.Duration.TotalSeconds })
                : Results.Json(new { error = "Incorrect credentials." }, statusCode: StatusCodes.Status401Unauthorized));

        // ─── Profile: everything the bank knows about the customer, whatever the screen ───
        app.MapGet("/profiles/me", (HttpContext http, Bank bank) =>
        {
            var u = bank.Users[User(http)];
            return Results.Ok(new
            {
                u.Id, u.FirstName, u.LastName, u.Email, u.Segment, u.AdvisorId, u.Accounts,
                Address = "12 Example Street, 75011 Paris", BirthDate = new DateOnly(1990, 5, 17),
                Preferences = new { Language = "en", Notifications = true, Theme = "light" }
            });
        });

        // ─── Accounts ───
        app.MapGet("/accounts/{id:guid}", (Guid id, HttpContext http, Bank bank) =>
            bank.Account(id) is not { } account ? Results.NotFound()
            : account.Owner != User(http) ? Forbidden()
            : Results.Ok(account));

        app.MapGet("/accounts/{id:guid}/operations", (Guid id, int? page, int? size, HttpContext http, Bank bank) =>
        {
            if (bank.Account(id) is not { } account)
                return Results.NotFound();
            if (account.Owner != User(http))
                return Forbidden();
            var (p, t) = (Math.Max(page ?? 1, 1), Math.Clamp(size ?? 20, 1, 100));
            var (operations, total) = bank.Operations(id, p, t);
            return Results.Ok(new { page = p, size = t, total, operations });
        });

        // ─── Advisors, offers ───
        app.MapGet("/advisors/{id}", (string id, Bank bank) =>
            bank.Advisors.TryGetValue(id, out var c) ? Results.Ok(c) : Results.NotFound());

        app.MapGet("/offers", (string? segment, Bank bank, Chaos chaos) =>
            chaos.OffersDown
                ? Results.Problem("Offers service unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(bank.Offers.Where(o => segment is null || o.Segment == segment)));

        // ─── Transfers ───
        app.MapPost("/transfers", (TransferRequest r, HttpContext http, Bank bank, TimeProvider clock) =>
        {
            if (bank.Account(r.Source) is { } source && source.Owner != User(http))
                return Forbidden();
            try
            {
                var account = bank.Transfer(r.Source, r.Target, r.Amount, clock.GetUtcNow());
                return Results.Ok(new { account.Id, account.Balance });
            }
            catch (KeyNotFoundException e)
            {
                return Results.NotFound(new { error = e.Message });
            }
            catch (InvalidOperationException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });

        // ─── Demo ───
        app.MapGet("/chaos", (Chaos chaos) => chaos);
        app.MapPut("/chaos", (ChaosRequest r, Chaos chaos) =>
        {
            chaos.LatencyMs = r.LatencyMs ?? chaos.LatencyMs;
            chaos.OffersDown = r.OffersDown ?? chaos.OffersDown;
            return Results.Ok(chaos);
        });
    }
}
