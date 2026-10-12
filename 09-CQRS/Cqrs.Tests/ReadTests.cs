using Cqrs.Events;
using Cqrs.Read;
using Cqrs.Read.Projections;
using Cqrs.Read.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Cqrs.Tests;

/// <summary>The read side is tested without the write side: it is given events, its views are queried.</summary>
public class ReadTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RecordedEvent[] History =
    [
        new(1, new AccountOpened(Alice, "Alice", 1000m, T0)),
        new(2, new AccountOpened(Bob, "Bob", 500m, T0.AddMinutes(1))),
        new(3, new MoneyWithdrawn(Alice, 100m, 900m, T0.AddMinutes(2))),
        new(4, new TransferMade(Guid.NewGuid(), Alice, Bob, 250m, 650m, 750m, T0.AddMinutes(3)))
    ];

    private static async Task<IServiceProvider> ProjectHistory()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection().AddReadSide(_ => databaseName).BuildServiceProvider();
        using var scope = services.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<Projector>();
        foreach (var @event in History)
            await projector.Apply(@event);
        return services;
    }

    private static async Task<T> Ask<T>(IServiceProvider services, IQuery<T> query)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<QueryBus>().Ask(query);
    }

    [Fact]
    public async Task The_statement_is_ready_to_display()
    {
        var services = await ProjectHistory();

        var statement = await Ask(services, new GetStatement(Alice));

        Assert.Equal(650m, statement!.Balance);
        Assert.Equal(3, statement.OperationCount);
        Assert.Equal(["Transfer to Bob", "Withdrawal", "Account opening"], statement.LatestOperations.Select(o => o.Label));
    }

    [Fact]
    public async Task The_history_contains_labels_already_written()
    {
        var services = await ProjectHistory();

        var operations = await Ask(services, new ListOperations(Bob, Page: 1, Size: 10));

        Assert.Equal(new OperationView("Transfer received from Alice", 250m, 750m, T0.AddMinutes(3)), operations[0]);
    }

    [Fact]
    public async Task The_dashboard_is_maintained_as_events_flow_in()
    {
        var services = await ProjectHistory();

        var dashboard = await Ask(services, new GetDashboard());

        Assert.Equal(new DashboardView(AccountCount: 2, TotalOutstanding: 1400m, TransferCount: 1, TotalTransferredAmount: 250m), dashboard);
    }

    [Fact]
    public async Task Replaying_an_already_projected_event_has_no_effect()
    {
        var services = await ProjectHistory();

        using (var scope = services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Projector>().Apply(History[2]);  // the withdrawal, again

        Assert.Equal(650m, (await Ask(services, new GetStatement(Alice)))!.Balance);
        Assert.Equal(1400m, (await Ask(services, new GetDashboard())).TotalOutstanding);
    }
}
