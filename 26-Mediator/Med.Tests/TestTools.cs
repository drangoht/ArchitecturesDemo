using Med.Bank;
using Med.Bank.Data;
using Med.Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Tests;

/// <summary>The complete API, started in memory, with its own database.</summary>
public class BankApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("DatabaseName", Guid.NewGuid().ToString());
}

/// <summary>
/// The bank without HTTP: the same registration as the API, a fresh database, the demo data.
/// Each Send runs in its own scope, like each HTTP request.
/// </summary>
public sealed class TestBank : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestBank(Action<IServiceCollection>? configure = null)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddBank(_ => databaseName);
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<BankDb>().SeedDemoData();
    }

    public IServiceProvider Services => _provider;

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    public void Dispose() => _provider.Dispose();
}

// What the API returns, read back in the tests.
public record AccountJson(Guid Id, string Owner, decimal Balance, List<OperationJson> LatestOperations, List<string> Notifications);

public record OperationJson(string Label, decimal Amount);
