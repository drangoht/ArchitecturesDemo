using System.Reflection;
using Res.Bank;
using Res.FraudSimulator;
using Res.Resilience;

namespace Res.Tests;

// The strategies are generic, the bank does not know how they are arranged, and only the API knows the simulator.
public class ArchitectureTests
{
    private static string[] InternalDependencies(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Res.")).Order()];

    [Fact]
    public void The_strategies_know_nothing_of_the_bank()
    {
        Assert.Empty(InternalDependencies(typeof(CircuitBreaker).Assembly));
    }

    [Fact]
    public void The_simulated_partner_knows_nothing_of_the_bank()
    {
        Assert.Empty(InternalDependencies(typeof(FraudPartner).Assembly));
    }

    [Fact]
    public void The_bank_only_knows_the_exceptions_of_the_strategies_never_the_simulator()
    {
        Assert.Equal(["Res.Resilience"], InternalDependencies(typeof(TransferService).Assembly));
    }

    [Fact]
    public void The_fraud_client_has_no_resilience_code_of_its_own()
    {
        var fields = typeof(FraudClient).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.FieldType);

        Assert.Equal([typeof(HttpClient)], fields);
    }
}
