using Cqrs.Write;
using Cqrs.Write.Commands;
using Cqrs.Write.Domain;
using Cqrs.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Tests;

public class WriteTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    // ─── The aggregate: we check the events it produces ───

    [Fact]
    public void Opening_an_account_produces_AccountOpened()
    {
        var id = Guid.NewGuid();

        var account = Account.Open(id, "Alice", 100m, Now);

        Assert.Equal([new AccountOpened(id, "Alice", 100m, Now)], account.Events);
    }

    [Fact]
    public void A_withdrawal_produces_MoneyWithdrawn_with_the_new_balance()
    {
        var account = Account.Open(Guid.NewGuid(), "Alice", 100m, Now);
        account.ExtractEvents();

        account.Withdraw(30m, Now);

        Assert.Equal(new MoneyWithdrawn(account.Id, 30m, 70m, Now), Assert.Single(account.Events));
    }

    [Fact]
    public void A_refused_withdrawal_produces_no_event()
    {
        var account = Account.Open(Guid.NewGuid(), "Alice", 100m, Now);
        account.ExtractEvents();

        Assert.Throws<BusinessRuleException>(() => account.Withdraw(500m, Now));
        Assert.Empty(account.Events);
    }

    // ─── The command pipeline ───

    private static IServiceProvider WriteSide()
    {
        var databaseName = Guid.NewGuid().ToString();
        return new ServiceCollection().AddLogging().AddWriteSide(_ => databaseName).BuildServiceProvider();
    }

    [Fact]
    public async Task An_invalid_command_is_rejected_before_reaching_the_handler()
    {
        var services = WriteSide();
        var bus = services.GetRequiredService<CommandBus>();

        var error = await Assert.ThrowsAsync<InvalidCommandException>(() =>
            bus.Send(new OpenAccount(Guid.NewGuid(), " ", 1m)));

        Assert.Equal(2, error.Errors.Count);
        Assert.Equal(0, await services.GetRequiredService<IEventStream>().LastSequence());
    }

    [Fact]
    public async Task The_commands_feed_the_event_log_in_order()
    {
        var services = WriteSide();
        var bus = services.GetRequiredService<CommandBus>();
        Guid alice = Guid.NewGuid(), bob = Guid.NewGuid();

        await bus.Send(new OpenAccount(alice, "Alice", 100m));
        await bus.Send(new OpenAccount(bob, "Bob", 50m));
        await bus.Send(new MakeTransfer(Guid.NewGuid(), alice, bob, 40m));

        var stream = await services.GetRequiredService<IEventStream>().ReadAfter(0, 10);
        Assert.Equal([1L, 2L, 3L], stream.Select(e => e.Sequence));
        var transfer = Assert.IsType<TransferMade>(stream[2].Event);
        Assert.Equal((60m, 90m), (transfer.SourceBalance, transfer.TargetBalance));
    }
}
