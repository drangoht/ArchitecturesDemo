using System.Reflection;
using Clean.AdaptateursInterface.Presenters;
using Clean.CasUtilisation.Retrait;
using Clean.Entites;

namespace Clean.Tests;

// La règle de dépendance : chaque cercle ne connaît que les cercles plus intérieurs.
public class ArchitectureTests
{
    private static string[] DependancesInternes(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(nom => nom.StartsWith("Clean.")).Order().ToArray();

    [Fact]
    public void Les_entites_ne_dependent_de_rien()
    {
        Assert.Empty(DependancesInternes(typeof(Compte).Assembly));
    }

    [Fact]
    public void Les_cas_d_utilisation_ne_dependent_que_des_entites()
    {
        Assert.Equal(["Clean.Entites"], DependancesInternes(typeof(RetraitInteracteur).Assembly));
    }

    [Fact]
    public void Les_adaptateurs_ne_dependent_pas_d_AspNetCore()
    {
        var references = typeof(VueHttp).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, nom => nom.StartsWith("Microsoft.AspNetCore"));
    }
}
