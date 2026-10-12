using Space.Bank;
using Space.Grid;

namespace Space.Tests;

/// <summary>The middleware and the business code together, without HTTP. Zero latencies; replication is held back by hand when needed.</summary>
public class PlatformTests : IAsyncLifetime
{
    private static readonly Guid Alice = DemoData.Alice, Bob = DemoData.Bob;

    private Platform _p = null!;

    public async Task InitializeAsync() => _p = await NewPlatform(new PlatformOptions());

    public async Task DisposeAsync() => await _p.DisposeAsync();

    private static async Task<Platform> NewPlatform(PlatformOptions options)
    {
        var platform = new Platform(options, Processing.Process);
        await platform.Start(DemoData.Accounts());
        return platform;
    }

    private async Task<(object Response, string Unit)> Send(IRequest request) => await _p.Send(request);

    private async Task<decimal> Balance(Guid account) => ((AccountDto)(await Send(new GetAccount(account))).Response).Balance;

    private IEnumerable<decimal> ReplicaBalances(Guid account) =>
        _p.Data.Replicas().Select(r => ((Account)r.Read(account.ToString())!.Value).Balance);

    private decimal DatabaseBalance(Guid account) => ((Account)_p.Database.Read(account.ToString())!.Value).Balance;

    [Fact]
    public async Task A_write_is_replicated_in_all_the_units_then_in_the_database()
    {
        await Send(new Withdraw(Alice, 100m));

        await _p.WaitForStabilization();

        Assert.Equal([900m, 900m], ReplicaBalances(Alice));
        Assert.Equal(900m, DatabaseBalance(Alice));
    }

    [Fact]
    public async Task Requests_never_wait_for_the_database()
    {
        _p.Database.Available = false;  // the database goes down…

        for (var i = 0; i < 10; i++)
            Assert.IsType<AccountDto>((await Send(new Withdraw(Alice, 10m))).Response);  // … the requests keep going

        Assert.Equal(10, _p.Pump.Pending);  // the pump keeps the writes
        Assert.Equal(1_000m, DatabaseBalance(Alice));
        Assert.Equal(1, _p.Database.Reads);  // the only read: the initial cold start

        _p.Database.Available = true;
        await _p.Pump.WaitUntilDrained();
        Assert.Equal(900m, DatabaseBalance(Alice));  // and the database catches up
    }

    [Fact]
    public async Task With_affinity_a_given_key_always_goes_to_the_same_unit()
    {
        await _p.Deployment.Start();
        await _p.Deployment.Start();

        var units = new HashSet<string>();
        for (var i = 0; i < 20; i++)
            units.Add((await Send(new GetAccount(Alice))).Unit);

        Assert.Single(units);
    }

    [Fact]
    public async Task With_round_robin_two_units_writing_the_same_data_collide()
    {
        _p.Messages.Mode = RoutingMode.RoundRobin;
        _p.Data.Suspend();  // each unit writes before having seen the other's write

        var first = await Send(new Withdraw(Alice, 100m));  // u2
        var second = await Send(new Withdraw(Alice, 50m));    // u1
        Assert.NotEqual(first.Unit, second.Unit);
        Assert.Equal(900m, ((AccountDto)first.Response).Balance);  // both clients receive "OK"…
        Assert.Equal(950m, ((AccountDto)second.Response).Balance);

        _p.Data.Resume();
        await _p.WaitForStabilization();

        // … but u2's write (the greatest version) overwrote u1's, everywhere: 100 € withdrawn instead of 150.
        Assert.Equal([900m, 900m], ReplicaBalances(Alice));
        Assert.Equal(900m, DatabaseBalance(Alice));  // the database received the losing write last, and ignored it
        Assert.Equal(_p.Data.Replicas()[0].Read(Alice.ToString())!.Version, _p.Database.Read(Alice.ToString())!.Version);
        Assert.Equal(1, _p.Data.LostUpdates);
        Assert.Contains(_p.Log(), e => e.Contains("COLLISION"));
    }

    [Fact]
    public async Task With_affinity_simultaneous_withdrawals_lose_nothing()
    {
        await using var p = await NewPlatform(new PlatformOptions { UnitsAtStartup = 3, ReplicationLatencyMs = 20 });

        var responses = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => p.Send(new Withdraw(Alice, 3m)))));
        await p.WaitForStabilization();

        Assert.All(responses, r => Assert.IsType<AccountDto>(r.Response));
        Assert.Single(responses.Select(r => r.Unit).Distinct());
        Assert.All(p.Data.Replicas(), r => Assert.Equal(400m, ((Account)r.Read(Alice.ToString())!.Value).Balance));
        Assert.Equal(400m, ((Account)p.Database.Read(Alice.ToString())!.Value).Balance);
        Assert.Equal(0, p.Data.LostUpdates);
    }

    [Fact]
    public async Task With_affinity_a_change_of_units_opens_a_collision_window()
    {
        var (_, former) = await Send(new Withdraw(Alice, 100m));
        await _p.Data.WaitForReplication();
        _p.Data.Suspend();
        await Send(new Withdraw(Alice, 100m));  // written by Alice's owner, not yet replicated

        await _p.Deployment.Stop(_p.Messages.Units().Single(u => u.Id == former));  // Alice changes owner
        var (response, newUnit) = await Send(new Withdraw(Alice, 50m));
        Assert.NotEqual(former, newUnit);
        Assert.Equal(850m, ((AccountDto)response).Balance);  // the new unit does not know the 2nd withdrawal yet

        _p.Data.Resume();
        await _p.WaitForStabilization();
        // 250 € of withdrawals accepted; depending on which write wins, 200 € or 150 € debited, never 250 €.
        Assert.Equal(1, _p.Data.LostUpdates);
        Assert.Contains(await Balance(Alice), new[] { 800m, 850m });
    }

    [Fact]
    public async Task Load_adds_units_and_calm_removes_them()
    {
        await using var p = await NewPlatform(new PlatformOptions { UnitsAtStartup = 1, CapacityPerUnit = 2, MaxUnits = 3 });
        p.Simulation.Close();  // the requests stay in progress

        var requests = Enumerable.Range(0, 4).Select(i => p.Send(new OpenAccount(Guid.NewGuid(), $"Customer {i}", 10m))).ToList();
        while (p.Messages.Units()[0].InProgress + p.Messages.Units()[0].Pending < 4)
            await Task.Delay(5);

        Assert.Equal("load 200%: u2 added", await p.Deployment.Evaluate());
        Assert.Equal("load 100%: u3 added", await p.Deployment.Evaluate());
        Assert.Null(await p.Deployment.Evaluate());  // ceiling of 3 units reached
        Assert.All(p.Messages.Units(), u => Assert.Equal(2, u.Replica.Count));  // complete copies of the grid

        p.Simulation.Open();
        await Task.WhenAll(requests);

        Assert.StartsWith("load 0%:", await p.Deployment.Evaluate());
        Assert.StartsWith("load 0%:", await p.Deployment.Evaluate());
        Assert.Null(await p.Deployment.Evaluate());  // minimum: 1 unit
        Assert.Single(p.Messages.Units());
    }

    [Fact]
    public async Task A_new_unit_receives_a_copy_of_the_grid_without_reading_the_database()
    {
        await Send(new Withdraw(Alice, 100m));
        await Send(new OpenAccount(Guid.NewGuid(), "Zoe", 50m));
        await _p.Data.WaitForReplication();

        var newUnit = await _p.Deployment.Start();

        var reference = _p.Messages.Units()[0].Replica.All().OrderBy(e => e.Key).Select(e => (e.Key, e.Version));
        Assert.Equal(reference, newUnit.Replica.All().OrderBy(e => e.Key).Select(e => (e.Key, e.Version)));
        Assert.Equal(1, _p.Database.Reads);
    }

    [Fact]
    public async Task The_failure_of_a_unit_loses_no_data()
    {
        var (_, unit) = await Send(new Withdraw(Alice, 100m));
        await _p.Data.WaitForReplication();

        _p.Deployment.Kill(_p.Messages.Units().Single(u => u.Id == unit));

        var (response, other) = await Send(new GetAccount(Alice));
        Assert.NotEqual(unit, other);
        Assert.Equal(900m, ((AccountDto)response).Balance);  // the data was also in the other replica
    }

    [Fact]
    public async Task The_requests_of_a_failed_unit_are_handed_to_another_one()
    {
        var unit = _p.Messages.Units().Single(u => u.Id == (_p.Send(new GetAccount(Alice)).Result.Unit));
        _p.Simulation.Close();
        var inProgress = Send(new Withdraw(Alice, 100m));
        var pending = Send(new Withdraw(Alice, 100m));
        while (unit.InProgress + unit.Pending < 2)
            await Task.Delay(5);

        _p.Deployment.Kill(unit);
        _p.Simulation.Open();

        Assert.All(await Task.WhenAll(inProgress, pending), r => Assert.NotEqual(unit.Id, r.Unit));
        Assert.Equal(800m, await Balance(Alice));
    }

    [Fact]
    public async Task After_a_total_failure_the_cold_start_reloads_the_grid_from_the_database()
    {
        await Send(new Withdraw(Alice, 100m));
        await _p.WaitForStabilization();

        foreach (var unit in _p.Messages.Units())
            _p.Deployment.Kill(unit);
        Assert.Empty(_p.Messages.Units());

        Assert.Equal(900m, await Balance(Alice));
        Assert.Equal(2, _p.Deployment.ColdStarts);  // the one at launch, and this one
        Assert.Equal(2, _p.Database.Reads);
    }

    [Fact]
    public async Task The_cold_start_waits_for_the_pump_to_have_written_everything()
    {
        _p.Database.Available = false;
        await Send(new Withdraw(Alice, 100m));  // written in the grid, not yet in the database
        await _p.Data.WaitForReplication();
        foreach (var unit in _p.Messages.Units())
            _p.Deployment.Kill(unit);

        await Assert.ThrowsAsync<DatabaseUnavailableException>(() => Send(new GetAccount(Alice)));  // neither unit nor database

        _p.Database.Available = true;
        // Without waiting for the pump, the new grid would start again from 1,000 €: the withdrawal would be lost.
        Assert.Equal(900m, await Balance(Alice));
    }

    [Fact]
    public async Task A_transfer_debits_in_the_source_unit_and_credits_in_the_target_unit()
    {
        while (Owner(Alice) == Owner(Bob))
            await _p.Deployment.Start();

        var (response, unit) = await Send(new Transfer(Alice, Bob, 100m));
        await _p.WaitForStabilization();

        Assert.Equal(Owner(Alice), unit);
        Assert.Equal(900m, ((TransferDto)response).SourceBalance);
        Assert.Equal(600m, await Balance(Bob));
        Assert.Equal(0, _p.Data.LostUpdates);
    }

    [Fact]
    public async Task Crossed_transfers_between_two_full_units_do_not_block()
    {
        await using var p = await NewPlatform(new PlatformOptions { CapacityPerUnit = 1 });
        while (p.Send(new GetAccount(Alice)).Result.Unit == p.Send(new GetAccount(Bob)).Result.Unit)
            await p.Deployment.Start();
        p.Simulation.Close();

        // Each unit fills its single slot with a transfer that will request a credit from the other unit.
        var transfers = Task.WhenAll(p.Send(new Transfer(Alice, Bob, 10m)), p.Send(new Transfer(Bob, Alice, 10m)));
        while (p.Messages.Units().Sum(u => u.InProgress) < 2)
            await Task.Delay(5);
        p.Simulation.Open();

        // If the credits waited for a slot, each unit would wait for the other forever.
        Assert.All(await transfers.WaitAsync(TimeSpan.FromSeconds(5)), r => Assert.IsType<TransferDto>(r.Response));
    }

    [Fact]
    public async Task A_credit_refused_by_the_target_unit_is_compensated()
    {
        _p.Messages.Mode = RoutingMode.RoundRobin;
        _p.Data.Suspend();  // the new account only exists in the replica of the unit that opened it
        var zoe = Guid.NewGuid();
        await Send(new OpenAccount(zoe, "Zoe", 0m));

        var refusals = new List<Refusal>();
        for (var i = 0; i < 2; i++)  // one of the two transfers goes through the unit that knows Zoe, and requests the credit from the other
            refusals.Add(Assert.IsType<Refusal>((await Send(new Transfer(Alice, zoe, 100m))).Response));
        _p.Data.Resume();
        await _p.WaitForStabilization();

        Assert.All(refusals, r => Assert.True(r.NotFound));
        Assert.Equal(1_000m, await Balance(Alice));
        var operations = ((Account)_p.Messages.Units()[0].Replica.Read(Alice.ToString())!.Value).Operations;
        Assert.Equal(["Initial deposit", "Transfer sent", "Transfer cancellation"], operations.Select(o => o.Label));
    }

    [Fact]
    public async Task Business_refusals_modify_nothing()
    {
        Assert.IsType<Refusal>((await Send(new Withdraw(Alice, 5_000m))).Response);
        Assert.IsType<Refusal>((await Send(new Deposit(Alice, -5m))).Response);
        Assert.IsType<Refusal>((await Send(new OpenAccount(Alice, "Alice", 0m))).Response);
        Assert.True(((Refusal)(await Send(new Transfer(Alice, Guid.NewGuid(), 10m))).Response).NotFound);
        await _p.WaitForStabilization();

        Assert.Equal(0, _p.Database.Writes);
        Assert.Equal(1_000m, await Balance(Alice));
    }

    private string Owner(Guid account) => _p.Send(new GetAccount(account)).Result.Unit;
}
