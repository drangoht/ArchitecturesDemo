using System.Reflection;
using Onion.Application;
using Onion.Domain.Model;

namespace Onion.Tests;

// Dependencies always point towards the center of the onion.
public class ArchitectureTests
{
    private static IEnumerable<string> InternalDependencies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Onion."));

    [Fact]
    public void The_domain_depends_on_nothing()
    {
        Assert.Empty(InternalDependencies(typeof(Account).Assembly));
    }

    [Fact]
    public void The_application_depends_only_on_the_domain()
    {
        Assert.Equal(["Onion.Domain"], InternalDependencies(typeof(AccountsAppService).Assembly));
    }
}
