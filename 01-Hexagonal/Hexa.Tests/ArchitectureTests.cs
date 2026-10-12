using Hexa.Hexagon.Domain;

namespace Hexa.Tests;

public class ArchitectureTests
{
    [Fact]
    public void The_hexagon_depends_on_no_adapter()
    {
        var dependencies = typeof(Account).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Hexa."));

        Assert.Empty(dependencies);
    }
}
