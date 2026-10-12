using System.Net.Http.Json;

namespace Cell.Router;

// Exchange format with the cells (copied: the router does not reference the code of the cells).
public sealed record AccountExport(Guid Id, string Label, decimal Balance);
public sealed record MovementExport(Guid AccountId, DateTimeOffset Date, string Label, decimal Amount);
public sealed record CustomerExport(string Id, string Name, List<AccountExport> Accounts, List<MovementExport> Movements, List<Guid> ProcessedOperations);
public sealed record Health(string Cell, string Version, bool Healthy, string Detail, int Customers);

public sealed record CellState(string Id, string Url, int Capacity, int Customers, bool Open, string Version, bool Reachable, bool? Healthy, string? Detail);
public sealed record DeploymentWave(int Wave, IReadOnlyList<string> Cells, string Result);
public sealed record DeploymentReport(string Version, string Status, IReadOnlyList<DeploymentWave> Waves);
public sealed record MigrationReport(string Customer, string From, string To, int Accounts, int Movements);

public sealed class ControlPlaneException(int status, string message) : Exception(message)
{
    public int Status => status;
}

/// <summary>
/// THE CONTROL PLANE: it decides (place a customer, add a cell, move a customer, deploy a version)
/// and writes the placement table. It can be down without the traffic stopping: the router and the cells
/// carry on with the table as it is. In production, it is a separate service, outside the path of the requests.
/// </summary>
public sealed class ControlPlane(CellRegistry registry, PlacementTable placement, IHttpClientFactory factory, IConfiguration configuration)
{
    private TimeSpan Observation => TimeSpan.FromMilliseconds(configuration.GetValue("Deployment:ObservationMs", 1_000));

    /// <summary>The least loaded open cell that still has room.</summary>
    public CellInfo? ChooseCell() => registry.All()
        .Where(c => c.Open && placement.Count(c.Id) < c.Capacity)
        .OrderBy(c => placement.Count(c.Id)).ThenBy(c => c.Id)
        .FirstOrDefault();

    /// <summary>
    /// A new customer. When all the cells are full, a cell is not enlarged: one is ADDED
    /// (POST /platform/cells). All the cells keep a known, tested size, and a bounded blast radius.
    /// </summary>
    public async Task<CellInfo> CreateCustomer(CustomerExport customer)
    {
        if (placement.CellOf(customer.Id) is not null)
            throw new ControlPlaneException(StatusCodes.Status409Conflict, $"Customer \"{customer.Id}\" already exists.");
        var cell = ChooseCell()
            ?? throw new ControlPlaneException(StatusCodes.Status503ServiceUnavailable, "All the cells are full: provision a new cell.");

        var response = await Client(cell).PostAsJsonAsync(Url(cell, "/internal/customers"), customer);
        await Check(response, cell);
        placement.Place(customer.Id, cell.Id);
        return cell;
    }

    /// <summary>A cell has just been provisioned (infrastructure created, application deployed): it is opened to new customers.</summary>
    public async Task<CellInfo> Register(CellInfo cell)
    {
        if (registry.Find(cell.Id) is not null)
            throw new ControlPlaneException(StatusCodes.Status409Conflict, $"Cell \"{cell.Id}\" already exists.");
        var health = await ReadHealth(cell)
            ?? throw new ControlPlaneException(StatusCodes.Status400BadRequest, $"Cell \"{cell.Id}\" does not answer on {cell.Url}.");
        var registered = cell with { Version = health.Version };
        registry.Add(registered);
        return registered;
    }

    /// <summary>No more new customers in this cell (before emptying it, or if it is nearing a limit).</summary>
    public bool Close(string id)
    {
        if (registry.Find(id) is not { } cell)
            return false;
        registry.Replace(cell with { Open = false });
        return true;
    }

    /// <summary>
    /// Move a customer (rebalancing, customer grown too big, cell to empty). The customer is unavailable for a few
    /// moments (503 + Retry-After); the other customers of both cells see nothing.
    /// </summary>
    public async Task<MigrationReport> Migrate(string customerId, string to)
    {
        var source = placement.CellOf(customerId) is { } id ? registry.Find(id)! : throw new ControlPlaneException(StatusCodes.Status404NotFound, $"Unknown customer \"{customerId}\".");
        var target = registry.Find(to) ?? throw new ControlPlaneException(StatusCodes.Status404NotFound, $"Unknown cell \"{to}\".");
        if (source.Id == target.Id)
            throw new ControlPlaneException(StatusCodes.Status400BadRequest, $"The customer is already in {target.Id}.");
        if (placement.Count(target.Id) >= target.Capacity)
            throw new ControlPlaneException(StatusCodes.Status400BadRequest, $"Cell {target.Id} is full.");

        placement.StartMigration(customerId);   // ① no more writes during the copy
        try
        {
            var export = await Client(source).GetFromJsonAsync<CustomerExport>(Url(source, $"/internal/customers/{customerId}/export"));
            await Check(await Client(target).PostAsJsonAsync(Url(target, "/internal/customers"), export), target);   // ② copy
            placement.Place(customerId, target.Id);                                                                 // ③ switch the routing
            await Client(source).DeleteAsync(Url(source, $"/internal/customers/{customerId}"));                       // ④ clean up
            return new MigrationReport(customerId, source.Id, target.Id, export!.Accounts.Count, export.Movements.Count);
        }
        finally
        {
            placement.EndMigration(customerId);
        }
    }

    /// <summary>
    /// Deployment in waves: first ONE cell (the canary), then the others. After each wave, the health is observed;
    /// at the slightest problem, the previous version is restored and the deployment stops. A bad deployment then only affects
    /// the customers of one cell, for a few moments.
    /// </summary>
    public async Task<DeploymentReport> Deploy(string version)
    {
        var cells = registry.All();
        List<CellInfo[]> waves = [[.. cells.Take(1)], [.. cells.Skip(1)]];
        var report = new List<DeploymentWave>();

        for (var n = 0; n < waves.Count; n++)
        {
            var wave = waves[n];
            var previous = wave.ToDictionary(c => c.Id, c => registry.Find(c.Id)!.Version);
            var problems = new List<string>();
            foreach (var cell in wave)
            {
                if (await ChangeVersion(cell, version) is { } refusal)
                    problems.Add($"{cell.Id}: {refusal}");
                else
                    registry.Replace(registry.Find(cell.Id)! with { Version = version });
            }

            await Task.Delay(Observation);   // let the real traffic and the probes speak
            foreach (var cell in wave)
            {
                var health = await ReadHealth(cell);
                if (health is not { Healthy: true })
                    problems.Add($"{cell.Id}: {health?.Detail ?? "unreachable"}");
            }

            if (problems.Count > 0)
            {
                foreach (var cell in wave)
                {
                    await ChangeVersion(cell, previous[cell.Id]);   // an unreachable cell will get its version back when it returns
                    registry.Replace(registry.Find(cell.Id)! with { Version = previous[cell.Id] });
                }
                report.Add(new DeploymentWave(n + 1, wave.Select(c => c.Id).ToList(), "rollback: " + string.Join("; ", problems.Distinct())));
                return new DeploymentReport(version, "Cancelled", report);
            }
            report.Add(new DeploymentWave(n + 1, wave.Select(c => c.Id).ToList(), "healthy"));
        }
        return new DeploymentReport(version, "Completed", report);
    }

    public async Task<IReadOnlyList<CellState>> State()
    {
        var states = await Task.WhenAll(registry.All().Select(async c =>
        {
            var health = await ReadHealth(c);
            return new CellState(c.Id, c.Url, c.Capacity, placement.Count(c.Id), c.Open, health?.Version ?? c.Version,
                health is not null, health?.Healthy, health?.Detail);
        }));
        return states;
    }

    /// <summary>null if the cell took the version, otherwise the reason for the refusal.</summary>
    private async Task<string?> ChangeVersion(CellInfo cell, string version)
    {
        try
        {
            var response = await Client(cell).PutAsJsonAsync(Url(cell, "/admin/version"), new { version });
            return response.IsSuccessStatusCode ? null : $"refused ({(int)response.StatusCode})";
        }
        catch (HttpRequestException)
        {
            return "unreachable";
        }
    }

    private async Task<Health?> ReadHealth(CellInfo cell)
    {
        try
        {
            var response = await Client(cell).GetAsync(Url(cell, "/health"));
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<Health>() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private HttpClient Client(CellInfo cell) => factory.CreateClient($"cell-{cell.Id}");

    private static Uri Url(CellInfo cell, string path) => new(new Uri(cell.Url), path);

    private static async Task Check(HttpResponseMessage response, CellInfo cell)
    {
        if (!response.IsSuccessStatusCode)
            throw new ControlPlaneException(StatusCodes.Status502BadGateway,
                $"Cell {cell.Id} refused ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }
}
