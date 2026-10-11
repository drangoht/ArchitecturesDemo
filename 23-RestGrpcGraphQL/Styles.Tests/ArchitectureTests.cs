using System.Reflection;
using Styles.Banque;
using Styles.Outillage.Protobuf;

namespace Styles.Tests;

public class ArchitectureTests
{
    private static string[] References(Assembly assemblage) =>
        assemblage.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Styles.") || n.StartsWith("Microsoft.AspNetCore")).ToArray();

    [Fact]
    public void Le_metier_ignore_tout_des_styles_d_API()
    {
        // Ni HTTP, ni Protobuf, ni GraphQL : on pourrait ajouter une quatrième API sans toucher au métier.
        Assert.Empty(References(typeof(ServiceBancaire).Assembly));
    }

    [Fact]
    public void L_outillage_ignore_la_banque_et_les_contrats_ignorent_le_metier()
    {
        Assert.DoesNotContain(References(typeof(Protobuf).Assembly), n => n.StartsWith("Styles."));
        Assert.Equal(["Styles.Outillage"], References(typeof(Contrats.Compte).Assembly));
    }
}
