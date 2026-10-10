using System.Reflection;
using Cqrs.Ecriture.Commandes;
using Cqrs.Evenements;
using Cqrs.Lecture.Requetes;

namespace Cqrs.Tests;

// Les deux côtés évoluent indépendamment : ils ne partagent que les événements.
public class ArchitectureTests
{
    private static string[] DependancesInternes(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(nom => nom.StartsWith("Cqrs.")).ToArray();

    [Fact]
    public void L_ecriture_ne_connait_pas_la_lecture()
    {
        Assert.Equal(["Cqrs.Evenements"], DependancesInternes(typeof(BusCommandes).Assembly));
    }

    [Fact]
    public void La_lecture_ne_connait_pas_l_ecriture()
    {
        Assert.Equal(["Cqrs.Evenements"], DependancesInternes(typeof(BusRequetes).Assembly));
    }

    [Fact]
    public void Les_evenements_ne_dependent_de_rien()
    {
        Assert.Empty(DependancesInternes(typeof(IEvenementDomaine).Assembly));
    }
}
