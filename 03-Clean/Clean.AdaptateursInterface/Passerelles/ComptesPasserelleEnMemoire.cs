using System.Collections.Concurrent;
using Clean.CasUtilisation.Passerelles;
using Clean.Entites;

namespace Clean.AdaptateursInterface.Passerelles;

/// <summary>Implémentation de la gateway (ici en mémoire ; une version EF Core irait au même endroit).</summary>
public class ComptesPasserelleEnMemoire : IComptesPasserelle
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ConcurrentDictionary<Guid, (string Titulaire, decimal Solde)> _lignes = new();

    public ComptesPasserelleEnMemoire(params Compte[] comptesInitiaux)
    {
        foreach (var compte in comptesInitiaux)
            _lignes[compte.Id] = (compte.Titulaire, compte.Solde);
    }

    public static ComptesPasserelleEnMemoire AvecDonneesDeDemo() =>
        new(new Compte(Alice, "Alice", 1000m), new Compte(Bob, "Bob", 500m));

    public Task<Compte?> Charger(Guid id) =>
        Task.FromResult(_lignes.TryGetValue(id, out var ligne) ? new Compte(id, ligne.Titulaire, ligne.Solde) : null);

    public Task Sauvegarder(Compte compte)
    {
        _lignes[compte.Id] = (compte.Titulaire, compte.Solde);
        return Task.CompletedTask;
    }
}
