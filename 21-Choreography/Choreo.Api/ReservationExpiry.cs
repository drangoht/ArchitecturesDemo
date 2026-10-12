using Choreo.Services;

namespace Choreo.Api;

public sealed class ApiOptions
{
    public int ReservationDurationSeconds { get; set; } = 30;
    public bool Loyalty { get; set; } = true;
    public double DuplicateProbability { get; set; }

    /// <summary>0 = no automatic expiry (the tests trigger it by hand).</summary>
    public int ExpiryPeriodMs { get; set; } = 1_000;
}

/// <summary>The timer of the Accounts service: it releases the reservations that nobody concluded in time.</summary>
public sealed class ReservationExpiry(Bank bank, ApiOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (options.ExpiryPeriodMs <= 0)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.ExpiryPeriodMs));
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
                bank.Accounts.ExpireReservations();
        }
        catch (OperationCanceledException)
        {
            // application shutdown
        }
    }
}
