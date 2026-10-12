using Hexa.Hexagon.Domain;

namespace Hexa.Adapters.Persistence;

public static class DemoData
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IEnumerable<Account> Accounts() =>
    [
        new Account(Alice, "Alice", 1000m),
        new Account(Bob, "Bob", 500m)
    ];
}
