using System.Reflection;

namespace Fig.Tests;

public class ArchitectureTests
{
    private static string[] References(Assembly assemblage) =>
        assemblage.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Fig.")).ToArray();

    [Fact]
    public void La_facade_et_le_nouveau_service_ne_connaissent_l_ancien_que_par_HTTP()
    {
        // La façade ne connaît ni l'un ni l'autre ; le nouveau service ne connaît pas l'ancien. Le jour où l'ancien est
        // éteint, on supprime son projet et la couche anti-corruption : rien d'autre ne doit compiler contre lui.
        Assert.Empty(References(typeof(Fig.Facade.Marqueur).Assembly));
        Assert.Empty(References(typeof(Fig.Nouveau.Marqueur).Assembly));
        Assert.Empty(References(typeof(Fig.Ancien.Marqueur).Assembly));
    }
}
