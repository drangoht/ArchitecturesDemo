using System.Reflection;
using Clean.InterfaceAdapters.Presenters;
using Clean.UseCases.Withdrawal;
using Clean.Entities;

namespace Clean.Tests;

// The dependency rule: each circle only knows the circles further inside.
public class ArchitectureTests
{
    private static string[] InternalDependencies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Clean.")).Order().ToArray();

    [Fact]
    public void The_entities_depend_on_nothing()
    {
        Assert.Empty(InternalDependencies(typeof(Account).Assembly));
    }

    [Fact]
    public void The_use_cases_depend_only_on_the_entities()
    {
        Assert.Equal(["Clean.Entities"], InternalDependencies(typeof(WithdrawalInteractor).Assembly));
    }

    [Fact]
    public void The_adapters_do_not_depend_on_AspNetCore()
    {
        var references = typeof(HttpView).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore"));
    }
}
