using Composants.Comptes;
using Composants.Virements;

namespace Composants.Tests;

/// <summary>
/// Garde-fou : la surface publique de chaque composant est exactement celle prévue.
/// Si quelqu'un rend public un type interne (DbContext, entité…), ce test échoue.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void Le_composant_Comptes_n_expose_que_son_interface_et_ses_contrats()
    {
        var publics = typeof(IComptesComposant).Assembly.GetExportedTypes().Select(t => t.Name).Order();

        Assert.Equal(
            ["CompteDto", "ComptesExtensions", "IComptesComposant", "Resultat", "StatutResultat"],
            publics);
    }

    [Fact]
    public void Le_composant_Virements_n_expose_que_son_interface_et_ses_contrats()
    {
        var publics = typeof(IVirementsComposant).Assembly.GetExportedTypes().Select(t => t.Name).Order();

        Assert.Equal(["IVirementsComposant", "VirementDto", "VirementsExtensions"], publics);
    }
}
