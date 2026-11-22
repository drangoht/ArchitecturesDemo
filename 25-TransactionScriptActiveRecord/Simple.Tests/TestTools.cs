using Microsoft.AspNetCore.Mvc.Testing;

namespace Simple.Tests;

/// <summary>A clock moved by hand: each operation gets its own date, so the order is certain.</summary>
public class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    public void Advance() => _now = _now.AddMinutes(1);

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>The complete API, started in memory: each instance has its own database, with the demo data.</summary>
public class BankApplication : WebApplicationFactory<Program>;

// What the API returns, read back in the tests (the same shape for both styles).
public record AccountJson(Guid Id, string Owner, decimal Balance, List<OperationJson> LatestOperations);

public record OperationJson(string Label, decimal Amount);

public record WithdrawalJson(Guid AccountId, decimal NewBalance);

public record TransferJson(decimal SourceBalance, decimal TargetBalance);

public record ErrorJson(string Error);
