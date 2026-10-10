using Hexa.Hexagone.Domaine;

namespace Hexa.Adapters.Persistance;

public static class DonneesDeDemo
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static IEnumerable<Compte> Comptes() =>
    [
        new Compte(Alice, "Alice", 1000m),
        new Compte(Bob, "Bob", 500m)
    ];
}
