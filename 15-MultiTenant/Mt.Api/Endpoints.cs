using Mt.Bank;
using Mt.Tenants;

namespace Mt.Api;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Alice and Bob exist at EACH bank, with the same identifiers but different balances:
    /// the same URL /accounts/1111… gives three answers depending on the tenant.
    /// </summary>
    public static IEnumerable<Account> Accounts(string tenant) => tenant switch
    {
        "alpha" => [new Account(Alice, "Alice", 1_000m), new Account(Bob, "Bob", 500m)],
        "beta" => [new Account(Alice, "Alice", 3_000m), new Account(Bob, "Bob", 200m)],
        "gamma" => [new Account(Alice, "Alice", 25_000m), new Account(Bob, "Bob", 1_500m)],
        _ => []
    };
}

internal static class Endpoints
{
    internal record OpeningRequest(string Owner, decimal InitialDeposit);
    internal record AmountRequest(decimal Amount);
    internal record TransferRequest(Guid Source, Guid Target, decimal Amount);
    internal record PlanRequest(Plan Plan);
    internal record FeeRequest(string Month);

    /// <summary>The customers' API: each route runs for the tenant of the request, without ever mentioning it.</summary>
    public static void MapBank(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant", (ITenantContext context) =>
        {
            var l = context.Require();
            return Results.Ok(new { l.Id, l.Name, l.Plan, l.Isolation, l.Rules, l.Quotas });
        });

        app.MapGet("/accounts", async (BankingService bank) => Results.Ok(await bank.List()));

        app.MapPost("/accounts", async (OpeningRequest r, BankingService bank) =>
        {
            var account = await bank.Open(r.Owner, r.InitialDeposit);
            return Results.Created($"/accounts/{account.Id}", account);
        });

        // Another bank's account answers 404, like an account that does not exist: its existence is not even revealed.
        app.MapGet("/accounts/{id:guid}", async (Guid id, BankingService bank) =>
            await bank.Get(id) is { } account ? Results.Ok(account) : Results.NotFound());

        app.MapGet("/accounts/{id:guid}/operations", async (Guid id, BankingService bank) =>
            Results.Ok(await bank.Operations(id)));

        app.MapPost("/accounts/{id:guid}/withdrawal", async (Guid id, AmountRequest r, BankingService bank) =>
            Results.Ok(await bank.Withdraw(id, r.Amount)));

        app.MapPost("/transfers", async (TransferRequest r, BankingService bank) =>
            Results.Ok(await bank.Transfer(r.Source, r.Target, r.Amount)));
    }

    /// <summary>The vendor's back office (outside any tenant).</summary>
    public static void MapPlatform(this IEndpointRouteBuilder app)
    {
        var platform = app.MapGroup("/platform");

        platform.MapGet("/tenants", (TenantCatalog catalog) =>
            Results.Ok(catalog.All().Select(View)));

        platform.MapGet("/tenants/{id}", (string id, TenantCatalog catalog) =>
            catalog.Find(id) is { } l ? Results.Ok(View(l)) : Results.NotFound());

        platform.MapPost("/tenants", async (TenantDefinition d, PlatformService service) =>
        {
            var tenant = await service.Register(d);
            return Results.Created($"/platform/tenants/{tenant.Id}", View(tenant));
        });

        platform.MapPost("/tenants/{id}/suspend", (string id, PlatformService service) =>
            service.Suspend(id, true) ? Results.NoContent() : Results.NotFound());

        platform.MapPost("/tenants/{id}/reactivate", (string id, PlatformService service) =>
            service.Suspend(id, false) ? Results.NoContent() : Results.NotFound());

        platform.MapPost("/tenants/{id}/plan", async (string id, PlanRequest r, PlatformService service) =>
            await service.ChangePlan(id, r.Plan) is { } report ? Results.Ok(report) : Results.NotFound());

        platform.MapDelete("/tenants/{id}", async (string id, PlatformService service) =>
            await service.Delete(id) is { } report ? Results.Ok(report) : Results.NotFound());

        platform.MapGet("/storage", async (PlatformService service) => Results.Ok(await service.Inventory()));

        platform.MapPost("/tasks/monthly-fees", async (FeeRequest r, PlatformService service) =>
            Results.Ok(await service.ChargeMonthlyFees(r.Month)));
    }

    private static object View(Tenant l) =>
        new { l.Id, l.Name, l.Plan, l.Isolation, Database = BankFactory.DatabaseName(l), l.Active, l.Rules, l.Quotas };
}
