using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mvu.Bank;
using Mvu.Runtime;
using Ui.Bank;

namespace Mvu.Tests;

public class ArchitectureTests
{
    [Fact]
    public void The_engine_knows_nothing_of_the_bank()
    {
        var references = typeof(Diff).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("Mvu.") || name.StartsWith("Ui."));
    }

    [Fact]
    public void Only_the_effects_call_the_bank_never_Update_nor_View()
    {
        static bool UsesBank(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters()).Any(p => p.ParameterType == typeof(IBank))
            || type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Any(f => f.FieldType == typeof(IBank))
            || type.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.ParameterType == typeof(IBank));

        Assert.False(UsesBank(typeof(BankScreen)));
        Assert.False(UsesBank(typeof(BankView)));
        Assert.True(UsesBank(typeof(BankEffects)));
    }

    [Fact]
    public void The_model_cannot_be_changed_in_place()
    {
        foreach (var property in typeof(Model).GetProperties())
        {
            var setter = property.SetMethod;
            var initOnly = setter?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) ?? true;
            Assert.True(initOnly, $"{property.Name} has a setter");
            if (property.PropertyType.IsGenericType && property.PropertyType.Name.Contains("List"))
                Assert.Equal(typeof(ImmutableList<>), property.PropertyType.GetGenericTypeDefinition());
        }
    }
}
