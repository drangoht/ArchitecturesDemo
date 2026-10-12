using System.Reflection;
using Layered.Data;
using Layered.Business;
using Layered.Presentation.Controllers;

namespace Layered.Tests;

// Dependencies point DOWNWARDS: presentation → business → data.
// Compare with 02-Onion, where the infrastructure (data) depends on the business code.
public class ArchitectureTests
{
    private static string[] InternalDependencies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Layered.")).ToArray();

    [Fact]
    public void The_data_layer_depends_on_nothing()
    {
        Assert.Empty(InternalDependencies(typeof(AccountEntity).Assembly));
    }

    [Fact]
    public void The_business_layer_depends_on_the_data_layer()
    {
        Assert.Equal(["Layered.Data"], InternalDependencies(typeof(AccountsService).Assembly));
    }

    [Fact]
    public void The_presentation_only_sees_the_layer_right_below()
    {
        Assert.Equal(["Layered.Business"], InternalDependencies(typeof(AccountsController).Assembly));
    }
}
