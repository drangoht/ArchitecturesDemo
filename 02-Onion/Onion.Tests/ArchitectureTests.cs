using System.Reflection;
using Onion.Application;
using Onion.Domain.Model;

namespace Onion.Tests;

// Les dépendances pointent toujours vers le centre de l'oignon.
public class ArchitectureTests
{
    private static IEnumerable<string> DependancesInternes(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(nom => nom.StartsWith("Onion."));

    [Fact]
    public void Le_domaine_ne_depend_de_rien()
    {
        Assert.Empty(DependancesInternes(typeof(Compte).Assembly));
    }

    [Fact]
    public void L_application_ne_depend_que_du_domaine()
    {
        Assert.Equal(["Onion.Domain"], DependancesInternes(typeof(ComptesAppService).Assembly));
    }
}
