using Microsoft.EntityFrameworkCore;
using Vertical.Api.Data;

namespace Vertical.Api.Features.Accounts;

/// <summary>
/// Read slice: direct projection to the response, without loading the entity or going through the domain.
/// Each slice picks the technique that suits it (here an optimized read-only LINQ query).
/// </summary>
public static class GetAccount
{
    public record Response(Guid Id, string Owner, decimal Balance, List<OperationView> LatestOperations);
    public record OperationView(string Label, decimal Amount, DateTime DateUtc);

    public static void Map(IEndpointRouteBuilder app) => app.MapGet("/accounts/{id:guid}", Execute);

    private static async Task<IResult> Execute(Guid id, BankDbContext db)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Owner, c.Balance })
            .FirstOrDefaultAsync();

        if (account is null)
            return Results.NotFound();

        var operations = await db.Operations
            .AsNoTracking()
            .Where(o => o.AccountId == id)
            .OrderByDescending(o => o.DateUtc)
            .Take(5)
            .Select(o => new OperationView(o.Label, o.Amount, o.DateUtc))
            .ToListAsync();

        return Results.Ok(new Response(account.Id, account.Owner, account.Balance, operations));
    }
}
