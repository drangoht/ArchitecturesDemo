using System.Reflection;
using Cqrs.Write.Commands;
using Cqrs.Events;
using Cqrs.Read.Queries;

namespace Cqrs.Tests;

// The two sides evolve independently: they only share the events.
public class ArchitectureTests
{
    private static string[] InternalDependencies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Cqrs.")).ToArray();

    [Fact]
    public void The_write_side_does_not_know_the_read_side()
    {
        Assert.Equal(["Cqrs.Events"], InternalDependencies(typeof(CommandBus).Assembly));
    }

    [Fact]
    public void The_read_side_does_not_know_the_write_side()
    {
        Assert.Equal(["Cqrs.Events"], InternalDependencies(typeof(QueryBus).Assembly));
    }

    [Fact]
    public void The_events_depend_on_nothing()
    {
        Assert.Empty(InternalDependencies(typeof(IDomainEvent).Assembly));
    }
}
