using System.Reflection;
using Modulaire.Comptes;
using Modulaire.Comptes.Contrats;
using Modulaire.Notifications;
using Modulaire.Virements;
using Modulaire.Virements.Contrats;

namespace Modulaire.Tests;

/// <summary>Les règles qui font d'un monolithe un monolithe MODULAIRE, vérifiées à chaque build.</summary>
public class ArchitectureTests
{
    public static TheoryData<string> Modules =>
    [
        typeof(ComptesModule).Assembly.GetName().Name!,
        typeof(VirementsModule).Assembly.GetName().Name!,
        typeof(NotificationsModule).Assembly.GetName().Name!
    ];

    private static IEnumerable<string> DependancesInternes(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(nom => nom.StartsWith("Modulaire."));

    [Theory]
    [MemberData(nameof(Modules))]
    public void Un_module_ne_reference_que_des_contrats_et_le_projet_partage(string module)
    {
        var interdites = DependancesInternes(Assembly.Load(module))
            .Where(nom => nom != "Modulaire.Partage" && !nom.EndsWith(".Contrats"));

        Assert.Empty(interdites);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Un_module_n_expose_que_sa_classe_IModule(string module)
    {
        var publics = Assembly.Load(module).GetExportedTypes();

        Assert.Equal(typeof(Modulaire.Partage.IModule), Assert.Single(publics).GetInterfaces().Single());
    }

    [Fact]
    public void Les_contrats_ne_dependent_d_aucun_module()
    {
        foreach (var contrats in new[] { typeof(IComptesApi).Assembly, typeof(VirementEffectue).Assembly })
            Assert.Equal(["Modulaire.Partage"], DependancesInternes(contrats));
    }
}
