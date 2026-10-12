using System.Reflection;
using Choreo.Bus;
using Choreo.Services;
using Choreo.Services.Contracts;
using Choreo.Services.Tracking;

namespace Choreo.Tests;

public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}

/// <summary>The services and the bus, without HTTP. The clock is manual: the expiry of the reservations is tested without waiting.</summary>
public class ChoreographyTests : IAsyncLifetime
{
    private static readonly Guid Alice = DemoData.Alice, Bob = DemoData.Bob, Carla = DemoData.Carla;

    private readonly ManualClock _clock = new();
    private Bank _bank = null!;

    public Task InitializeAsync()
    {
        _bank = Bank.Start(new BankOptions(), _clock);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _bank.DisposeAsync();

    private async Task<TransferState> Transfer(Guid source, Guid target, decimal amount, Bank? bank = null)
    {
        bank ??= _bank;
        var id = bank.Request(source, target, amount);
        await bank.Bus.WaitForCalm();
        return bank.Tracking.State(id)!;
    }

    private decimal Balance(Guid account) => _bank.Accounts.Get(account)!.Balance;
    private decimal Reserved(Guid account) => _bank.Accounts.Get(account)!.Reserved;
    private IEnumerable<string> Types(string transfer) => _bank.Bus.Log(transfer).Select(e => e.Type);

    [Fact]
    public async Task A_transfer_moves_forward_through_chain_reactions_without_anyone_driving_it()
    {
        var transfer = await Transfer(Alice, Carla, 100m);

        Assert.Equal(TransferStatus.Settled, transfer.Status);
        var events = _bank.Bus.Log(transfer.Id);
        Assert.Equal(["TransferRequested", "FundsReserved", "ComplianceApproved", "CurrenciesConverted", "TransferSettled", "CustomerNotified"],
            events.Select(e => e.Type));
        Assert.Equal(["API", "Accounts", "Compliance", "Exchange", "Accounts", "Notifications"], events.Select(e => e.Emitter));
        // Each one reacts to the previous one: the chain of causes is the only "plan" of the process.
        Assert.All(events.Skip(1).Zip(events), pair => Assert.Equal(pair.Second.Id, pair.First.Cause));
        Assert.Equal((49_900m, 1_108.32m), (Balance(Alice), Balance(Carla)));
        Assert.True(transfer.CustomerNotified);
    }

    [Fact]
    public async Task A_compliance_refusal_is_compensated_by_the_accounts_service()
    {
        var transfer = await Transfer(Alice, DemoData.Mallory, 50m);

        Assert.Equal((TransferStatus.Cancelled, "payee on blocklist"), (transfer.Status, transfer.Reason));
        Assert.Equal(["TransferRequested", "FundsReserved", "ComplianceRefused", "FundsReleased"], Types(transfer.Id));
        Assert.Equal((50_000m, 0m), (Balance(Alice), Reserved(Alice)));
    }

    [Fact]
    public async Task An_exchange_failure_is_compensated_too()
    {
        var transfer = await Transfer(Alice, DemoData.Yuki, 100m);

        Assert.Equal(TransferStatus.Cancelled, transfer.Status);
        Assert.Equal(["TransferRequested", "FundsReserved", "ComplianceApproved", "ExchangeFailed", "FundsReleased"], Types(transfer.Id));
        Assert.Equal(0m, Reserved(Alice));
    }

    [Fact]
    public async Task An_insufficient_balance_stops_everything_from_the_first_reaction()
    {
        var transfer = await Transfer(Bob, Alice, 900m);

        Assert.Equal(TransferStatus.Refused, transfer.Status);
        Assert.Equal(["TransferRequested", "FundsRefused"], Types(transfer.Id));
    }

    [Fact]
    public async Task A_big_transfer_waits_for_an_analyst_decision()
    {
        var approved = await Transfer(Alice, Bob, 15_000m);
        var refused = await Transfer(Alice, Bob, 12_000m);
        Assert.Equal(TransferStatus.AwaitingAnalyst, approved.Status);
        Assert.Equal(27_000m, Reserved(Alice));

        Assert.True(_bank.Compliance.Decide(approved.Id, approved: true));
        Assert.True(_bank.Compliance.Decide(refused.Id, approved: false));
        Assert.False(_bank.Compliance.Decide(refused.Id, approved: true));  // already decided
        await _bank.Bus.WaitForCalm();

        Assert.Equal(TransferStatus.Settled, _bank.Tracking.State(approved.Id)!.Status);
        Assert.Equal(TransferStatus.Cancelled, _bank.Tracking.State(refused.Id)!.Status);
        Assert.Equal((35_000m, 0m, 15_500m), (Balance(Alice), Reserved(Alice), Balance(Bob)));
    }

    [Fact]
    public async Task Reserved_money_is_no_longer_available_for_another_transfer()
    {
        await Transfer(Alice, Bob, 40_000m);  // awaiting an analyst: 40,000 € reserved, not yet debited

        var second = await Transfer(Alice, Bob, 15_000m);

        Assert.Equal(TransferStatus.Refused, second.Status);
        Assert.StartsWith("insufficient available balance", second.Reason);
        Assert.Equal(50_000m, Balance(Alice));
    }

    [Fact]
    public async Task A_failed_service_blocks_the_process_until_the_reservation_expires()
    {
        _bank.Exchange.Down = true;

        var transfer = await Transfer(Alice, Bob, 200m);

        // Nobody waits for the exchange: the event ends up in the dead letters, the transfer is left hanging, the money reserved.
        Assert.Equal("Exchange", _bank.Bus.DeadLetters().Single().Subscriber);
        Assert.Equal(TransferStatus.InProgress, transfer.Status);
        Assert.Equal(200m, Reserved(Alice));
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal([transfer.Id], _bank.Tracking.Stuck(_clock.GetUtcNow(), TimeSpan.FromSeconds(10)).Select(v => v.Id));

        _clock.Advance(TimeSpan.FromSeconds(11));  // 31 s: the reservation (30 s) expires
        Assert.Equal(1, _bank.Accounts.ExpireReservations());
        await _bank.Bus.WaitForCalm();

        Assert.Equal(TransferStatus.Abandoned, _bank.Tracking.State(transfer.Id)!.Status);
        Assert.Equal((50_000m, 0m), (Balance(Alice), Reserved(Alice)));
    }

    [Fact]
    public async Task After_the_expiry_neither_the_analyst_nor_a_late_message_settles_the_transfer()
    {
        var transfer = await Transfer(Alice, Bob, 15_000m);
        _clock.Advance(TimeSpan.FromSeconds(31));
        _bank.Accounts.ExpireReservations();
        await _bank.Bus.WaitForCalm();

        Assert.False(_bank.Compliance.Decide(transfer.Id, approved: true));  // compliance listened to the expiry

        // A late approval (the exchange being down, nothing follows): Tracking does not reopen a finished transfer.
        var reserved = (FundsReserved)_bank.Bus.Log(transfer.Id)[1];
        _bank.Exchange.Down = true;
        _bank.Bus.Publish(reserved.Next(new ComplianceApproved(reserved.Order), "Compliance"));
        await _bank.Bus.WaitForCalm();
        Assert.Equal(TransferStatus.Abandoned, _bank.Tracking.State(transfer.Id)!.Status);

        // A very late message (a slow exchange) arrives anyway: Accounts refuses to settle.
        _bank.Bus.Publish(reserved.Next(new CurrenciesConverted(reserved.Order with { TargetAmount = 15_000m }), "Exchange"));
        await _bank.Bus.WaitForCalm();

        Assert.Equal("TransferAbandoned", Types(transfer.Id).Last());
        Assert.Equal(TransferStatus.Abandoned, _bank.Tracking.State(transfer.Id)!.Status);
        Assert.Equal((50_000m, 500m), (Balance(Alice), Balance(Bob)));
    }

    [Fact]
    public async Task Events_delivered_twice_produce_their_effect_only_once()
    {
        await using var bank = Bank.Start(new BankOptions { Bus = new BusOptions { DuplicateProbability = 1.0 } }, _clock);

        for (var i = 0; i < 20; i++)
            await Transfer(Alice, Bob, 10m, bank);

        Assert.Equal(49_800m, bank.Accounts.Get(Alice)!.Balance);
        Assert.Equal(700m, bank.Accounts.Get(Bob)!.Balance);
        Assert.Equal(20, bank.Bus.Log().Count(e => e is TransferSettled));  // each service reacted only once
        Assert.Equal(20, bank.Notifications.Sent().Count);
        Assert.Equal(20, bank.Loyalty!.Points(Alice));
    }

    [Fact]
    public async Task After_the_point_of_no_return_a_failed_notification_is_retried_never_compensated()
    {
        _bank.Notifications.Sabotage(3);
        var retried = await Transfer(Alice, Bob, 10m);
        _bank.Notifications.Sabotage(10);
        var givenUp = await Transfer(Alice, Bob, 10m);

        Assert.True(retried.CustomerNotified);  // 3 failures, then the 4th attempt goes through
        Assert.Equal(TransferStatus.Settled, givenUp.Status);  // the money is gone: nothing is undone
        Assert.False(givenUp.CustomerNotified);
        Assert.Equal(("Notifications", "TransferSettled"), (_bank.Bus.DeadLetters().Single().Subscriber, _bank.Bus.DeadLetters().Single().Event.Type));
    }

    [Fact]
    public async Task Adding_a_subscriber_changes_nothing_for_the_others()
    {
        await using var without = Bank.Start(new BankOptions { Loyalty = false }, _clock);

        var withLoyalty = await Transfer(Alice, Carla, 100m);
        var withoutLoyalty = await Transfer(Alice, Carla, 100m, without);

        Assert.Equal(Types(withLoyalty.Id), without.Bus.Log(withoutLoyalty.Id).Select(e => e.Type));
        Assert.Equal(10, _bank.Loyalty!.Points(Alice));
        Assert.DoesNotContain("Loyalty", without.Bus.Subscribers().Select(a => a.Subscriber));
    }

    [Fact]
    public void No_service_knows_the_others()
    {
        // Architecture test: a service only handles its own types, the contracts, the bus and the common tools.
        string[] services = ["Accounts", "Compliance", "Exchange", "Notifications", "Loyalty", "Tracking"];
        var assembly = typeof(Bank).Assembly;
        static string? Service(Type t) => t.Namespace?.StartsWith("Choreo.Services.") == true ? t.Namespace.Split('.')[2] : null;

        foreach (var type in assembly.GetTypes().Where(t => services.Contains(Service(t))))
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var used = type.GetFields(all).Select(f => f.FieldType)
                .Concat(type.GetProperties(all).Select(p => p.PropertyType))
                .Concat(type.GetConstructors(all).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
                .Concat(type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
                .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : [t]);

            foreach (var usedType in used)
                Assert.True(Service(usedType) is null || Service(usedType) == "Contracts" || Service(usedType) == Service(type),
                    $"{type.FullName} uses {usedType.FullName}");
        }
    }
}
