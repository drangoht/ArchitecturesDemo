using Choreo.Bus;
using Choreo.Services.Exchange;
using Choreo.Services.Accounts;
using Choreo.Services.Compliance;
using Choreo.Services.Contracts;
using Choreo.Services.Loyalty;
using Choreo.Services.Notifications;
using Choreo.Services.Tracking;

namespace Choreo.Services;

public sealed record BankOptions
{
    public TimeSpan ReservationDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The service added afterwards: it can be plugged in or not, the others know nothing about it.</summary>
    public bool Loyalty { get; init; } = true;

    public BusOptions Bus { get; init; } = new();
}

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Carla = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Yuki = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid Mallory = ComplianceService.Mallory;
}

/// <summary>
/// The assembly: a bus, services that subscribe to it. It is NOT an orchestrator: after the first event,
/// it does nothing more; it only serves to start the system and to launch transfers.
/// </summary>
public sealed class Bank : IAsyncDisposable
{
    private Bank(BankOptions options, TimeProvider clock)
    {
        Clock = clock;
        Bus = new Choreo.Bus.Bus(options.Bus, clock);
        Accounts = new AccountsService(Bus, clock, options.ReservationDuration);
        Compliance = new ComplianceService(Bus);
        Exchange = new ExchangeService(Bus);
        Notifications = new NotificationsService(Bus);
        Loyalty = options.Loyalty ? new LoyaltyService(Bus) : null;
        Tracking = new TrackingService(Bus);
    }

    public TimeProvider Clock { get; }
    public Choreo.Bus.Bus Bus { get; }
    public AccountsService Accounts { get; }
    public ComplianceService Compliance { get; }
    public ExchangeService Exchange { get; }
    public NotificationsService Notifications { get; }
    public LoyaltyService? Loyalty { get; }
    public TrackingService Tracking { get; }

    public static Bank Start(BankOptions? options = null, TimeProvider? clock = null)
    {
        var bank = new Bank(options ?? new BankOptions(), clock ?? TimeProvider.System);
        bank.Tracking.Register();
        bank.Accounts.Register();
        bank.Compliance.Register();
        bank.Exchange.Register();
        bank.Notifications.Register();
        bank.Loyalty?.Register();

        bank.Accounts.Open(DemoData.Alice, "Alice", "EUR", 50_000m);
        bank.Accounts.Open(DemoData.Bob, "Bob", "EUR", 500m);
        bank.Accounts.Open(DemoData.Carla, "Carla", "USD", 1_000m);
        bank.Accounts.Open(DemoData.Yuki, "Yuki", "JPY", 100_000m);
        bank.Accounts.Open(DemoData.Mallory, "Mallory", "EUR", 0m);
        return bank;
    }

    /// <summary>The only gesture "from outside": publishing that a transfer is requested. The rest no longer depends on anyone.</summary>
    public string Request(Guid source, Guid target, decimal amount, string? id = null)
    {
        var transfer = id ?? $"v-{Guid.NewGuid():N}"[..10];
        Bus.Publish(new TransferRequested(new Order(source, target, amount)) { Correlation = transfer, Emitter = "API" });
        return transfer;
    }

    /// <summary>Waits until the transfer is finished (or awaiting an analyst), according to Tracking.</summary>
    public async Task<TransferState?> Wait(string id, TimeSpan? timeout = null)
    {
        var limit = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < limit)
        {
            if (Tracking.State(id) is { } state && (state.IsFinished || state.Status == TransferStatus.AwaitingAnalyst))
                return state;
            await Task.Delay(5);
        }
        return Tracking.State(id);
    }

    public ValueTask DisposeAsync() => Bus.DisposeAsync();
}
