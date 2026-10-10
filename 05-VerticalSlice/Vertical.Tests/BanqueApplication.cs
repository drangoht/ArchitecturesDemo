using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vertical.Tests;

/// <summary>
/// L'API complète, démarrée en mémoire, avec sa propre base : chaque test part des données de démo.
/// En Vertical Slice, on teste surtout chaque tranche de bout en bout, par HTTP.
/// </summary>
public class BanqueApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("NomBase", Guid.NewGuid().ToString());
}
