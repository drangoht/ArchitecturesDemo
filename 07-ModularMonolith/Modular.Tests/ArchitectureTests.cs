using System.Reflection;
using Modular.Accounts;
using Modular.Accounts.Contracts;
using Modular.Notifications;
using Modular.Transfers;
using Modular.Transfers.Contracts;

namespace Modular.Tests;

/// <summary>The rules that make a monolith a MODULAR monolith, checked on every build.</summary>
public class ArchitectureTests
{
    public static TheoryData<string> Modules =>
    [
        typeof(AccountsModule).Assembly.GetName().Name!,
        typeof(TransfersModule).Assembly.GetName().Name!,
        typeof(NotificationsModule).Assembly.GetName().Name!
    ];

    private static IEnumerable<string> InternalDependencies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Modular."));

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_only_references_contracts_and_the_shared_project(string module)
    {
        var forbidden = InternalDependencies(Assembly.Load(module))
            .Where(name => name != "Modular.Shared" && !name.EndsWith(".Contracts"));

        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void A_module_only_exposes_its_IModule_class(string module)
    {
        var publics = Assembly.Load(module).GetExportedTypes();

        Assert.Equal(typeof(Modular.Shared.IModule), Assert.Single(publics).GetInterfaces().Single());
    }

    [Fact]
    public void The_contracts_depend_on_no_module()
    {
        foreach (var contracts in new[] { typeof(IAccountsApi).Assembly, typeof(TransferMade).Assembly })
            Assert.Equal(["Modular.Shared"], InternalDependencies(contracts));
    }
}
