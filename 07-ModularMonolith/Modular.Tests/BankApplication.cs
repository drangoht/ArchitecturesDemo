using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Modular.Tests;

/// <summary>The complete monolith, in memory, with fresh databases. Services can be replaced for a test.</summary>
public class BankApplication(Action<IServiceCollection>? replacements = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseName", Guid.NewGuid().ToString());
        if (replacements is not null)
            builder.ConfigureTestServices(replacements);
    }
}
