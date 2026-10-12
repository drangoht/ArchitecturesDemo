namespace Bff.Services;

/// <summary>Marker of the assembly, for the tests (each project of the demo has its own Program).</summary>
public sealed class Marker;

public sealed record User(
    string Id, string Password, string FirstName, string LastName, string Email, string Segment, string AdvisorId, Guid[] Accounts);

public sealed record Account(
    Guid Id, string Owner, string Iban, string Label, string Type, string Currency,
    decimal Balance, decimal WithdrawalLimit, DateOnly OpeningDate, string Branch);

public sealed record Operation(Guid Id, Guid AccountId, DateTimeOffset Date, string RawLabel, string Category, decimal Amount);

public sealed record Advisor(string Id, string Name, string Phone, string Email, string Branch);

public sealed record Offer(string Id, string Segment, string Title, string Text, string Image, DateOnly ValidUntil);

/// <summary>
/// The data of the "business" services. These APIs are generic: they know nothing of the screens that call them,
/// return complete objects and are queried one by one. That is precisely why a BFF is useful.
/// </summary>
public sealed class Bank
{
    public static readonly Guid AliceChecking = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid BobChecking = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AliceSavings = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Account> _accounts = [];
    private readonly List<Operation> _operations = [];

    public IReadOnlyDictionary<string, User> Users { get; } = new Dictionary<string, User>
    {
        // Demo passwords: this fake identity provider has no other use.
        ["alice"] = new("alice", "demo", "Alice", "Martin", "alice@example.com", "individual", "c1", [AliceChecking, AliceSavings]),
        ["bob"] = new("bob", "demo", "Bob", "Durand", "bob@example.com", "young", "c2", [BobChecking])
    };

    public IReadOnlyDictionary<string, Advisor> Advisors { get; } = new Dictionary<string, Advisor>
    {
        ["c1"] = new("c1", "Claire Dubois", "01 23 45 67 89", "claire.dubois@example.com", "Paris Bastille"),
        ["c2"] = new("c2", "Karim Benali", "04 72 00 00 00", "karim.benali@example.com", "Lyon Part-Dieu")
    };

    public IReadOnlyList<Offer> Offers { get; } =
    [
        new("o1", "individual", "Fixed-rate mortgage", "Finance your main residence… (full text of the offer, conditions, representative example)", "/images/mortgage.jpg", new DateOnly(2026, 12, 31)),
        new("o2", "individual", "Home insurance", "Protect your home… (full text, cover, excesses)", "/images/home.jpg", new DateOnly(2026, 11, 30)),
        new("o3", "young", "Free youth card", "A free card up to age 25… (full text)", "/images/card.jpg", new DateOnly(2027, 6, 30))
    ];

    public Bank()
    {
        Add(new Account(AliceChecking, "alice", "FR76 3000 4000 0100 0000 1111 182", "Checking account", "Checking", "EUR", 1_000m, 500m, new DateOnly(2018, 3, 12), "Paris Bastille"), 25);
        Add(new Account(AliceSavings, "alice", "FR76 3000 4000 0100 0000 4444 512", "Savings account", "Savings", "EUR", 8_000m, 0m, new DateOnly(2019, 9, 1), "Paris Bastille"), 4);
        Add(new Account(BobChecking, "bob", "FR76 3000 4000 0200 0000 2222 097", "Checking account", "Checking", "EUR", 500m, 300m, new DateOnly(2024, 1, 15), "Lyon Part-Dieu"), 15);
    }

    public Account? Account(Guid id)
    {
        lock (_lock)
            return _accounts.GetValueOrDefault(id);
    }

    /// <summary>Operations of the account, most recent first, page by page.</summary>
    public (IReadOnlyList<Operation> Page, int Total) Operations(Guid accountId, int page, int size)
    {
        lock (_lock)
        {
            var all = _operations.Where(o => o.AccountId == accountId).OrderByDescending(o => o.Date).ToList();
            return (all.Skip((page - 1) * size).Take(size).ToList(), all.Count);
        }
    }

    public Account Transfer(Guid source, Guid target, decimal amount, DateTimeOffset date)
    {
        lock (_lock)
        {
            if (amount <= 0 || source == target)
                throw new InvalidOperationException("Positive amount and different accounts required.");
            if (!_accounts.TryGetValue(source, out var s) || !_accounts.TryGetValue(target, out var c))
                throw new KeyNotFoundException("Account not found.");
            if (s.Balance < amount)
                throw new InvalidOperationException($"Insufficient balance ({s.Balance:0.00} €).");

            _accounts[source] = s with { Balance = s.Balance - amount };
            _accounts[target] = c with { Balance = c.Balance + amount };
            _operations.Add(new Operation(Guid.NewGuid(), source, date, $"SEPA TRANSFER SENT {c.Label.ToUpperInvariant()}", "Transfer", -amount));
            _operations.Add(new Operation(Guid.NewGuid(), target, date, $"SEPA TRANSFER RECEIVED {s.Label.ToUpperInvariant()}", "Transfer", amount));
            return _accounts[source];
        }
    }

    private static readonly (string Label, string Category, decimal Min, decimal Max)[] Templates =
    [
        ("CARD CARREFOUR {0} PARIS 15", "Groceries", 12m, 95m),
        ("CARD BOULANGERIE DU PONT {0}", "Groceries", 2m, 12m),
        ("DIRECT DEBIT EDF", "Housing", 45m, 90m),
        ("DIRECT DEBIT FREE MOBILE", "Telecom", 10m, 20m),
        ("CARD SNCF INTERNET {0}", "Transport", 25m, 120m),
        ("CARD FNAC.COM {0}", "Leisure", 15m, 80m),
        ("CARD AMAZON EU SARL {0}", "Shopping", 8m, 60m),
        ("ATM WITHDRAWAL {0} LYON", "Withdrawal", 20m, 60m)
    ];

    /// <summary>A deterministic history (fixed seed): the tests and the examples of the docs stay stable.</summary>
    private void Add(Account account, int operationCount)
    {
        _accounts[account.Id] = account;
        var random = new Random(account.Id.GetHashCode());
        var date = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < operationCount; i++)
        {
            date = date.AddHours(-random.Next(20, 60));
            if (account.Type == "Savings")
            {
                _operations.Add(new Operation(Guid.NewGuid(), account.Id, date, "SEPA TRANSFER FROM CHECKING ACCOUNT", "Savings", 100m));
                continue;
            }
            if (i % 10 == 4)
            {
                _operations.Add(new Operation(Guid.NewGuid(), account.Id, date, "SEPA TRANSFER SALARY ACME SAS", "Income", 2_100m));
                continue;
            }
            var (label, category, min, max) = Templates[random.Next(Templates.Length)];
            var amount = Math.Round(min + (decimal)random.NextDouble() * (max - min), 2);
            _operations.Add(new Operation(Guid.NewGuid(), account.Id, date, string.Format(label, date.ToString("ddMM")), category, -amount));
        }
    }
}
