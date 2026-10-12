using System.Numerics;

namespace Pipeline.Bank;

/// <summary>A transfer order, as it flows between the filters.</summary>
public sealed record Transfer(string Reference, string Payer, string Payee, decimal Amount, string Currency, decimal AmountEur = 0);

/// <summary>What comes out of the import pipeline: an executed transfer.</summary>
public sealed record ExecutedTransfer(string Reference, string Payer, string Payee, decimal AmountEur, decimal PayerBalance);

public sealed record AccountDto(string Iban, string Owner, decimal Balance, int Operations);

/// <summary>The bank's accounts, and the references of the transfers already executed (to never execute twice).</summary>
public sealed class Ledger
{
    private readonly Dictionary<string, (string Owner, decimal Balance, int Operations)> _accounts = new();
    private readonly HashSet<string> _executed = new();
    private readonly Lock _lock = new();

    public void Open(string iban, string owner, decimal balance)
    {
        lock (_lock) _accounts[iban] = (owner, balance, 0);
    }

    public AccountDto? Get(string iban)
    {
        lock (_lock)
            return _accounts.TryGetValue(iban, out var c) ? new AccountDto(iban, c.Owner, c.Balance, c.Operations) : null;
    }

    /// <summary>All the balances: a transfer moves money, it neither creates nor destroys any.</summary>
    public IReadOnlyDictionary<string, decimal> Balances()
    {
        lock (_lock) return _accounts.ToDictionary(c => c.Key, c => c.Value.Balance);
    }

    public bool AlreadyExecuted(string reference)
    {
        lock (_lock) return _executed.Contains(reference);
    }

    public int ExecutedCount
    {
        get { lock (_lock) return _executed.Count; }
    }

    /// <summary>Debit and credit together, or nothing. Returns the reason for the refusal, or null.</summary>
    public string? Transfer(Transfer v, out decimal payerBalance)
    {
        lock (_lock)
        {
            payerBalance = 0;
            if (v.AmountEur <= 0)
                return "amount not converted";  // precondition: the Convert currency filter came before
            if (!_executed.Add(v.Reference))
                return "duplicate";
            if (!_accounts.TryGetValue(v.Payer, out var payer) || !_accounts.TryGetValue(v.Payee, out var payee))
            {
                _executed.Remove(v.Reference);
                return "unknown account";
            }
            if (v.AmountEur > payer.Balance)
            {
                _executed.Remove(v.Reference);
                return "insufficient balance";
            }
            _accounts[v.Payer] = payer with { Balance = payer.Balance - v.AmountEur, Operations = payer.Operations + 1 };
            _accounts[v.Payee] = payee with { Balance = payee.Balance + v.AmountEur, Operations = payee.Operations + 1 };
            payerBalance = payer.Balance - v.AmountEur;
            return null;
        }
    }
}

/// <summary>IBAN check and creation (ISO 13616 check digits: modulo 97).</summary>
public static class Iban
{
    public static bool IsValid(string iban) =>
        iban.Length is >= 15 and <= 34 && iban.All(char.IsAsciiLetterOrDigit) && Remainder(iban[4..] + iban[..4]) == 1;

    /// <summary>Computes the check digits of an IBAN from the BBAN (for the demo data).</summary>
    public static string Make(string bban, string country = "FR") => $"{country}{98 - Remainder(bban + country + "00"):D2}{bban}";

    private static int Remainder(string text) =>
        (int)(BigInteger.Parse(string.Concat(text.ToUpperInvariant().Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()))) % 97);
}

public static class DemoData
{
    public const string Alice = "FR7630006000011234567890189";
    public const string Bob = "FR1230006000019876543210452";

    /// <summary>An existing account, but on the fraud check's blocklist.</summary>
    public const string Suspect = "FR4330004000030000000000991";

    public static Ledger Ledger()
    {
        var ledger = new Ledger();
        ledger.Open(Alice, "Alice", 1_000m);
        ledger.Open(Bob, "Bob", 500m);
        ledger.Open(Suspect, "Shell company", 0m);
        return ledger;
    }
}
