using Components.Accounts;
using Components.Transfers;

namespace Components.Tests;

/// <summary>
/// Safety net: the public surface of each component is exactly the planned one.
/// If someone makes an internal type public (DbContext, entity…), this test fails.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void The_Accounts_component_only_exposes_its_interface_and_contracts()
    {
        var publics = typeof(IAccountsComponent).Assembly.GetExportedTypes().Select(t => t.Name).Order();

        Assert.Equal(
            ["AccountDto", "AccountsExtensions", "IAccountsComponent", "Result", "ResultStatus"],
            publics);
    }

    [Fact]
    public void The_Transfers_component_only_exposes_its_interface_and_contracts()
    {
        var publics = typeof(ITransfersComponent).Assembly.GetExportedTypes().Select(t => t.Name).Order();

        Assert.Equal(["ITransfersComponent", "TransferDto", "TransfersExtensions"], publics);
    }
}
