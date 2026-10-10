using Hexa.Hexagone.Domaine;

namespace Hexa.Tests;

public class ArchitectureTests
{
    [Fact]
    public void L_hexagone_ne_depend_d_aucun_adapter()
    {
        var dependances = typeof(Compte).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(nom => nom.StartsWith("Hexa."));

        Assert.Empty(dependances);
    }
}
