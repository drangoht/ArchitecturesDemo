using System.Globalization;
using System.Text;

namespace Bff.Mobile;

// Short responses: a small screen, a slow mobile network billed by volume.
public sealed record MobileAccount(Guid Id, string Name, decimal Balance);
public sealed record MobileOperation(DateOnly Date, string Label, decimal Amount);
public sealed record MobileOffer(string Title);
public sealed record MobileHome(string FirstName, decimal TotalBalance, IReadOnlyList<MobileAccount> Accounts,
    IReadOnlyList<MobileOperation> LatestOperations, MobileOffer? Offer = null);
public sealed record MobilePage(IReadOnlyList<MobileOperation> Operations, string? Next);

/// <summary>The screens of the mobile application: what each screen displays, no more, no less.</summary>
public sealed class Screens(ServicesClient services, IConfiguration configuration)
{
    private const int PageSize = 10;

    private TimeSpan OfferBudget => TimeSpan.FromMilliseconds(configuration.GetValue("Bff:OfferBudgetMs", 300));

    /// <summary>
    /// The home screen. v1: balances and 3 latest operations. v2 (new version of the application): in addition,
    /// an offer banner. Both versions coexist, because not all the phones update: it is the BFF
    /// that absorbs this evolution, the services have not changed.
    /// </summary>
    public async Task<MobileHome> Home(string token, int version)
    {
        var profile = await services.Profile(token);

        var accounts = profile.Accounts.Select(id => services.Account(id, token)).ToList();
        var operations = profile.Accounts.Select(id => services.Operations(id, 1, 3, token)).ToList();
        var offer = version >= 2 ? Optional(c => services.Offers(profile.Segment, token, c), OfferBudget) : Task.FromResult<DownstreamOffer[]?>(null);
        Task[] calls = [.. accounts, .. operations, offer];
        await Task.WhenAll(calls);

        var accountViews = accounts.Select(c => new MobileAccount(c.Result.Id, c.Result.Label, c.Result.Balance)).ToList();
        var latest = operations.SelectMany(o => o.Result.Operations)
            .OrderByDescending(o => o.Date).Take(3).Select(View).ToList();
        var firstOffer = offer.Result?.FirstOrDefault();

        return new MobileHome(profile.FirstName, accountViews.Sum(c => c.Balance), accountViews, latest,
            firstOffer is null ? null : new MobileOffer(firstOffer.Title));
    }

    /// <summary>
    /// Infinite scrolling: the application asks for "the rest" with an opaque cursor, without knowing the pagination
    /// of the services (page/size), which the BFF can change without updating the application.
    /// </summary>
    public async Task<MobilePage> Operations(Guid account, string? cursor, string token)
    {
        var page = ReadPage(cursor);
        var result = await services.Operations(account, page, PageSize, token);
        var next = page * PageSize < result.Total ? Cursor(page + 1) : null;
        return new MobilePage(result.Operations.Select(View).ToList(), next);
    }

    private static MobileOperation View(DownstreamOperation o) => new(DateOnly.FromDateTime(o.Date.UtcDateTime), ShortLabel(o.RawLabel), o.Amount);

    // Hexadecimal: opaque for the application, and with no character to escape in a URL.
    private static string Cursor(int page) => Convert.ToHexString(Encoding.UTF8.GetBytes($"page:{page}"));

    private static int ReadPage(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return 1;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromHexString(cursor));
            return text.StartsWith("page:") && int.TryParse(text[5..], out var page) && page > 0 ? page : 1;
        }
        catch (FormatException)
        {
            return 1;
        }
    }

    private static readonly (string Prefix, string? Replacement)[] Prefixes =
    [
        ("ATM WITHDRAWAL", "Cash withdrawal"), ("SEPA TRANSFER SENT", "Transfer sent"), ("SEPA TRANSFER RECEIVED", "Transfer received"),
        ("SEPA TRANSFER FROM", "Transfer received"), ("SEPA TRANSFER SALARY", "Salary"), ("DIRECT DEBIT", null), ("CARD", null)
    ];

    private static readonly HashSet<string> Acronyms = ["EDF", "SNCF", "EU"];
    private static readonly HashSet<string> UselessWords = ["SARL", "SAS"];

    /// <summary>"CARD CARREFOUR 0910 PARIS 15" → "Carrefour": on a phone, the raw bank label does not fit.</summary>
    public static string ShortLabel(string raw)
    {
        foreach (var (prefix, replacement) in Prefixes)
        {
            if (!raw.StartsWith(prefix + " "))
                continue;
            if (replacement is not null)
                return replacement;
            var words = raw[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .TakeWhile(m => !m.Any(char.IsDigit))
                .Where(m => !UselessWords.Contains(m))
                .Select(m => Acronyms.Contains(m) ? m : CultureInfo.GetCultureInfo("en-GB").TextInfo.ToTitleCase(m.ToLowerInvariant()));
            return string.Join(' ', words);
        }
        return raw;
    }

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
