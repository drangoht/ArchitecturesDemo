namespace Bff.Web;

/// <summary>Marker of the assembly, for the tests.</summary>
public sealed class Marker;

public sealed record AdvisorView(string Name, string Phone, string Email, string Branch);
public sealed record CustomerView(string FirstName, string LastName, string Email, AdvisorView Advisor);
public sealed record OperationView(DateTimeOffset Date, string Label, string Category, decimal Amount);
public sealed record AccountView(Guid Id, string Label, string Type, string Iban, decimal Balance, string Currency, int OperationCount, IReadOnlyList<OperationView> Operations);
public sealed record SpendingView(string Category, decimal Total);
public sealed record OfferView(string Title, string Text, string Image);

/// <summary>Exactly what the site's home screen displays, in a single response.</summary>
public sealed record DashboardView(
    CustomerView Customer, IReadOnlyList<AccountView> Accounts, IReadOnlyList<SpendingView> SpendingByCategory,
    IReadOnlyList<OfferView>? Offers, IReadOnlyList<string> Degraded);

/// <summary>
/// The aggregation: one call from the browser, seven calls to the services, made on the server side (fast network, in parallel).
/// Without a BFF, it is the browser that would chain these seven calls, then assemble and compute by itself.
/// </summary>
public sealed class Dashboard(ServicesClient services, IConfiguration configuration)
{
    private TimeSpan OffersBudget => TimeSpan.FromMilliseconds(configuration.GetValue("Bff:OffersBudgetMs", 800));

    public async Task<DashboardView> Build(string token)
    {
        // ① The profile first: it says which accounts to display, which advisor, which offer segment.
        var profile = await services.Profile(token);

        // ② Then all the rest IN PARALLEL: the total duration is that of the slowest call, not the sum.
        var advisor = services.Advisor(profile.AdvisorId, token);
        var offers = Optional(cancellation => services.Offers(profile.Segment, token, cancellation), OffersBudget);
        var accounts = profile.Accounts.Select(id => AccountWithOperations(id, token)).ToList();
        Task[] calls = [advisor, offers, .. accounts];
        await Task.WhenAll(calls);

        // ③ Assemble and compute for the screen: the browser only has to display.
        var accountViews = accounts.Select(c => c.Result).ToList();
        var spending = accountViews.SelectMany(c => c.Operations).Where(o => o.Amount < 0)
            .GroupBy(o => o.Category)
            .Select(g => new SpendingView(g.Key, -g.Sum(o => o.Amount)))
            .OrderByDescending(d => d.Total).ToList();
        var c = advisor.Result;

        return new DashboardView(
            new CustomerView(profile.FirstName, profile.LastName, profile.Email, new AdvisorView(c.Name, c.Phone, c.Email, c.Branch)),
            accountViews,
            spending,
            offers.Result?.Select(o => new OfferView(o.Title, o.Text, o.Image)).ToList(),
            offers.Result is null ? ["offers"] : []);
    }

    private async Task<AccountView> AccountWithOperations(Guid id, string token)
    {
        var account = services.Account(id, token);
        var operations = services.Operations(id, 10, token);
        await Task.WhenAll(account, operations);
        var (c, o) = (account.Result, operations.Result);
        return new AccountView(c.Id, c.Label, c.Type, c.Iban, c.Balance, c.Currency, o.Total,
            o.Operations.Select(x => new OperationView(x.Date, x.RawLabel, x.Category, x.Amount)).ToList());
    }

    /// <summary>
    /// The offers are secondary: down or too slow, the screen is displayed without them ("degraded")
    /// instead of failing entirely. The time budget prevents a slow service from slowing down the whole page.
    /// </summary>
    private static async Task<T?> Optional<T>(Func<CancellationToken, Task<T>> call, TimeSpan budget) where T : class
    {
        using var timeout = new CancellationTokenSource(budget);
        try
        {
            return await call(timeout.Token);
        }
        catch (Exception e) when (e is DownstreamServiceException or HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }
}
