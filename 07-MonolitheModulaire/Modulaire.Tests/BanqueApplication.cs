using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Modulaire.Tests;

/// <summary>Le monolithe complet, en mémoire, avec des bases neuves. On peut remplacer des services pour un test.</summary>
public class BanqueApplication(Action<IServiceCollection>? remplacements = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("NomBase", Guid.NewGuid().ToString());
        if (remplacements is not null)
            builder.ConfigureTestServices(remplacements);
    }
}
