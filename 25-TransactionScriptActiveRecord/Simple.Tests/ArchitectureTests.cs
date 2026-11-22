using System.Reflection;
using Simple.Data;
using Simple.Records;
using Simple.Scripts;

namespace Simple.Tests;

// No layer between the code and the database: both styles depend on it directly, and only on it.
// Compare with 02-Onion, where the database depends on the business code.
public class ArchitectureTests
{
    private static string[] InternalDependencies(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Simple.")).Order()];

    [Fact]
    public void The_database_depends_on_nothing()
    {
        Assert.Empty(InternalDependencies(typeof(Database).Assembly));
    }

    [Fact]
    public void The_scripts_only_know_the_database()
    {
        Assert.Equal(["Simple.Data"], InternalDependencies(typeof(BankScripts).Assembly));
    }

    [Fact]
    public void The_active_records_only_know_the_database()
    {
        Assert.Equal(["Simple.Data"], InternalDependencies(typeof(Account).Assembly));
    }

    [Fact]
    public void An_active_record_can_only_change_its_balance_through_its_rules()
    {
        var publicSetters = typeof(Account).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name);

        Assert.Empty(publicSetters);
    }

    [Fact]
    public void An_active_record_can_only_be_created_by_Find_or_Open()
    {
        Assert.Empty(typeof(Account).GetConstructors());
    }
}
