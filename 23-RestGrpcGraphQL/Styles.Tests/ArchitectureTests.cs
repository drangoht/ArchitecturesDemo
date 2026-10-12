using System.Reflection;
using Styles.Bank;
using Styles.Tooling.Protobuf;

namespace Styles.Tests;

public class ArchitectureTests
{
    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Styles.") || n.StartsWith("Microsoft.AspNetCore")).ToArray();

    [Fact]
    public void The_business_code_knows_nothing_about_API_styles()
    {
        // Neither HTTP, nor Protobuf, nor GraphQL: a fourth API could be added without touching the business code.
        Assert.Empty(References(typeof(BankService).Assembly));
    }

    [Fact]
    public void The_tooling_ignores_the_bank_and_the_contracts_ignore_the_business_code()
    {
        Assert.DoesNotContain(References(typeof(Protobuf).Assembly), n => n.StartsWith("Styles."));
        Assert.Equal(["Styles.Tooling"], References(typeof(Contracts.Account).Assembly));
    }
}
