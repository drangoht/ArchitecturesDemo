namespace Cell.Router;

public static class DemoData
{
    /// <summary>Placed in this order, they spread out: alice and david in c1, bob and emma in c2, carla in c3.</summary>
    public static readonly CustomerExport[] Customers =
    [
        Customer("alice", "Alice", "11111111-1111-1111-1111-111111111111", 1_000m),
        Customer("bob", "Bob", "22222222-2222-2222-2222-222222222222", 500m),
        Customer("carla", "Carla", "33333333-3333-3333-3333-333333333333", 800m),
        Customer("david", "David", "55555555-5555-5555-5555-555555555555", 300m),
        Customer("emma", "Emma", "66666666-6666-6666-6666-666666666666", 1_200m)
    ];

    private static CustomerExport Customer(string id, string name, string account, decimal balance) =>
        new(id, name, [new AccountExport(Guid.Parse(account), "Checking account", balance)], [], []);
}

/// <summary>
/// At startup, registers the demo customers through the same path as a new customer (the control plane picks the cell).
/// The cells can start after the router: we retry until they answer.
/// </summary>
public sealed class DemoSeeding(ControlPlane plane, IConfiguration configuration, ILogger<DemoSeeding> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken stopping)
    {
        if (!configuration.GetValue("Router:SeedDemo", true))
            return;
        var limit = DateTime.UtcNow.AddSeconds(30);
        foreach (var customer in DemoData.Customers)
        {
            while (true)
            {
                try
                {
                    var cell = await plane.CreateCustomer(customer);
                    logger.LogInformation("Customer {Customer} placed in cell {Cell}", customer.Id, cell.Id);
                    break;
                }
                catch (Exception e) when (e is HttpRequestException or ControlPlaneException { Status: 502 } && DateTime.UtcNow < limit)
                {
                    await Task.Delay(500, stopping);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken stopping) => Task.CompletedTask;
}
