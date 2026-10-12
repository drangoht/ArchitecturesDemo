using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vertical.Tests;

/// <summary>
/// The complete API, started in memory, with its own database: each test starts from the demo data.
/// With Vertical Slice, each slice is mostly tested end to end, over HTTP.
/// </summary>
public class BankApplication : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("DatabaseName", Guid.NewGuid().ToString());
}
