using System.Reflection;
using Med.Api;
using Med.Bank;
using Med.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Med.Tests;

/// <summary>
/// With a mediator, the compiler no longer sees who calls whom: Send compiles for a request without any handler.
/// These tests put back the checks the compiler used to do.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Bank = typeof(BankRegistration).Assembly;

    private static string[] InternalDependencies(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(name => name.StartsWith("Med.")).Order()];

    private static bool Implements(Type type, Type openContract) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openContract);

    [Fact]
    public void The_mediator_knows_nothing_of_the_bank()
    {
        Assert.Empty(InternalDependencies(typeof(IMediator).Assembly));
    }

    [Fact]
    public void The_bank_only_depends_on_the_mediator()
    {
        Assert.Equal(["Med.Mediator"], InternalDependencies(Bank));
    }

    [Fact]
    public void Every_request_of_the_bank_has_exactly_one_handler()
    {
        using var bank = new TestBank();
        using var scope = bank.Services.CreateScope();

        var requests = Bank.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && Implements(t, typeof(IRequest<>)));
        foreach (var request in requests)
        {
            var response = request.GetInterfaces().Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                .GetGenericArguments()[0];
            var handlers = scope.ServiceProvider.GetServices(typeof(IRequestHandler<,>).MakeGenericType(request, response));
            Assert.True(handlers.Count() == 1, $"{request.Name}: {handlers.Count()} handler(s)");
        }
    }

    [Fact]
    public void The_handlers_are_internal_so_the_API_can_only_reach_them_through_the_mediator()
    {
        var publicHandlers = Bank.GetExportedTypes()
            .Where(t => Implements(t, typeof(IRequestHandler<,>)) || Implements(t, typeof(INotificationHandler<>)))
            .Select(t => t.Name);

        Assert.Empty(publicHandlers);
    }

    [Fact]
    public void A_handler_never_depends_on_another_handler()
    {
        var handlerContracts = new[] { typeof(IRequestHandler<,>), typeof(INotificationHandler<>) };
        var handlers = Bank.GetTypes().Where(t => handlerContracts.Any(c => Implements(t, c)));

        var offenders = handlers
            .SelectMany(h => h.GetConstructors().SelectMany(c => c.GetParameters()), (h, p) => (Handler: h.Name, p.ParameterType))
            .Where(x => x.ParameterType.IsGenericType && handlerContracts.Contains(x.ParameterType.GetGenericTypeDefinition()))
            .Select(x => x.Handler);

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_API_routes_only_know_the_mediator()
    {
        var parameterTypes = typeof(BankEndpoints).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType);

        Assert.DoesNotContain(parameterTypes, t => t.Assembly == Bank && t.Name.EndsWith("Handler"));
    }
}
