using System.Reflection;
using Couches.Donnees;
using Couches.Metier;
using Couches.Presentation.Controllers;

namespace Couches.Tests;

// Les dépendances DESCENDENT : présentation → métier → données.
// Comparer avec 02-Onion, où c'est l'infrastructure (données) qui dépend du métier.
public class ArchitectureTests
{
    private static string[] DependancesInternes(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(nom => nom.StartsWith("Couches.")).ToArray();

    [Fact]
    public void La_couche_donnees_ne_depend_de_rien()
    {
        Assert.Empty(DependancesInternes(typeof(CompteEntite).Assembly));
    }

    [Fact]
    public void Le_metier_depend_de_la_couche_donnees()
    {
        Assert.Equal(["Couches.Donnees"], DependancesInternes(typeof(ComptesService).Assembly));
    }

    [Fact]
    public void La_presentation_ne_voit_que_la_couche_juste_en_dessous()
    {
        Assert.Equal(["Couches.Metier"], DependancesInternes(typeof(ComptesController).Assembly));
    }
}
