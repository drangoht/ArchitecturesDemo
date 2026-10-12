using System.Reflection;

namespace Fig.Tests;

public class ArchitectureTests
{
    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Fig.")).ToArray();

    [Fact]
    public void The_facade_and_the_modern_service_only_know_the_legacy_system_through_HTTP()
    {
        // The facade knows neither one nor the other; the modern service does not know the legacy system. The day the legacy system is
        // switched off, its project and the anti-corruption layer are deleted: nothing else must compile against it.
        Assert.Empty(References(typeof(Fig.Facade.Marker).Assembly));
        Assert.Empty(References(typeof(Fig.Modern.Marker).Assembly));
        Assert.Empty(References(typeof(Fig.Legacy.Marker).Assembly));
    }
}
