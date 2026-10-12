namespace Styles.Bank;

/// <summary>The starting data: Alice has three accounts (enough to see REST's "1 + N"), Bob just one.</summary>
internal static class DataSet
{
    public const string Alice = "alice";
    public const string Bob = "bob";

    public static void Load(BankService bank)
    {
        bank.AddCustomer(new Customer(Alice, "Alice Martin", "alice.martin@example.com"));
        bank.AddCustomer(new Customer(Bob, "Bob Durand", "bob.durand@example.com"));

        bank.AddAccount(new Account("10001", Alice, "Current account", AccountType.Current,
            "FR76 3000 4000 0110 0010 0010 001", new DateOnly(2015, 3, 2), 500m, 0, 0));
        bank.AddAccount(new Account("10002", Alice, "Savings account", AccountType.Savings,
            "FR76 3000 4000 0110 0010 0020 002", new DateOnly(2016, 9, 15), 0m, 0, 0));
        bank.AddAccount(new Account("10003", Alice, "Joint account", AccountType.Current,
            "FR76 3000 4000 0110 0010 0030 003", new DateOnly(2021, 6, 1), 200m, 0, 0));
        bank.AddAccount(new Account("20001", Bob, "Current account", AccountType.Current,
            "FR76 3000 4000 0220 0020 0010 004", new DateOnly(2019, 1, 10), 300m, 0, 0));

        // September 2026, Paris time (UTC+2).
        var paris = TimeSpan.FromHours(2);
        DateTimeOffset On(int day, int hour) => new(2026, 9, day, hour, 0, 0, paris);

        bank.AddOperation("10001", On(1, 9), "Salary", 2800.00m);
        bank.AddOperation("10001", On(3, 12), "Rent", -950.00m);
        bank.AddOperation("10001", On(5, 18), "Groceries", -86.40m);
        bank.AddOperation("10001", On(9, 20), "Restaurant", -42.50m);
        bank.AddOperation("10001", On(12, 10), "Electricity", -71.15m);
        bank.AddOperation("10001", On(20, 17), "Bookshop", -23.90m);

        bank.AddOperation("10002", On(1, 8), "Carried forward", 8000.00m);
        bank.AddOperation("10002", On(2, 10), "Deposit", 150.00m);

        bank.AddOperation("10003", On(1, 8), "Alice's contribution", 600.00m);
        bank.AddOperation("10003", On(1, 9), "Paul's contribution", 600.00m);
        bank.AddOperation("10003", On(14, 11), "Home insurance", -38.20m);
        bank.AddOperation("10003", On(28, 16), "Holidays", -412.00m);

        bank.AddOperation("20001", On(1, 9), "Salary", 1900.00m);
        bank.AddOperation("20001", On(4, 12), "Rent", -720.00m);
        bank.AddOperation("20001", On(15, 19), "Phone", -19.99m);
    }
}
