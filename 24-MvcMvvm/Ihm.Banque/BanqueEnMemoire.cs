namespace Ihm.Banque;

/// <summary>
/// La banque, en mémoire, avec une latence réglable pour imiter un service distant (0 dans les tests et le site,
/// quelques centaines de millisecondes dans l'application de bureau, pour voir l'interface attendre).
/// </summary>
public sealed class BanqueEnMemoire : IBanque
{
    public static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid LivretAlice = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly object _verrou = new();
    private readonly TimeProvider _horloge;
    private readonly TimeSpan _latence;
    private readonly Dictionary<Guid, Compte> _comptes = [];
    private readonly Dictionary<Guid, List<Operation>> _operations = [];

    public BanqueEnMemoire(TimeProvider horloge, TimeSpan latence = default)
    {
        _horloge = horloge;
        _latence = latence;
        var ouverture = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(2));
        Ouvrir(Alice, "Alice", "Compte courant", 1_000m, ouverture);
        Ouvrir(LivretAlice, "Alice", "Livret A", 8_000m, ouverture);
        Ouvrir(Bob, "Bob", "Compte courant", 500m, ouverture);
    }

    public async Task<IReadOnlyList<Compte>> ComptesAsync(CancellationToken annulation = default)
    {
        await Attendre(annulation);
        lock (_verrou)
            return _comptes.Values.OrderBy(c => c.Titulaire).ThenBy(c => c.Libelle).ToList();
    }

    public async Task<Compte> CompteAsync(Guid id, CancellationToken annulation = default)
    {
        await Attendre(annulation);
        lock (_verrou)
            return Trouver(id);
    }

    public async Task<IReadOnlyList<Operation>> OperationsAsync(Guid compte, CancellationToken annulation = default)
    {
        await Attendre(annulation);
        lock (_verrou)
        {
            Trouver(compte);
            return _operations[compte].AsEnumerable().Reverse().ToList();
        }
    }

    public async Task<Virement> VirerAsync(Guid source, Guid cible, decimal montant, CancellationToken annulation = default)
    {
        await Attendre(annulation);
        var erreur = ReglesVirement.VerifierMontant(montant) ?? ReglesVirement.VerifierComptes(source, cible);
        if (erreur is not null)
            throw new ErreurMetier(erreur);
        lock (_verrou)
        {
            var debite = Trouver(source);
            var credite = Trouver(cible);
            if (ReglesVirement.VerifierSolde(debite.Solde, montant) is { } refus)
                throw new ErreurMetier(refus);
            var maintenant = _horloge.GetUtcNow();
            debite = Mouvement(debite, maintenant, $"Virement vers {credite.Titulaire} ({credite.Libelle})", -montant);
            credite = Mouvement(credite, maintenant, $"Virement de {debite.Titulaire} ({debite.Libelle})", montant);
            return new Virement(source, cible, montant, debite.Solde, credite.Solde);
        }
    }

    private Task Attendre(CancellationToken annulation) =>
        _latence > TimeSpan.Zero ? Task.Delay(_latence, _horloge, annulation) : Task.CompletedTask;

    private Compte Trouver(Guid id) =>
        _comptes.GetValueOrDefault(id) ?? throw new ErreurMetier($"Compte {id} inconnu.");

    private void Ouvrir(Guid id, string titulaire, string libelle, decimal depot, DateTimeOffset date)
    {
        _comptes[id] = new Compte(id, titulaire, libelle, 0m);
        _operations[id] = [];
        Mouvement(_comptes[id], date, "Dépôt à l'ouverture", depot);
    }

    private Compte Mouvement(Compte compte, DateTimeOffset date, string libelle, decimal montant)
    {
        _operations[compte.Id].Add(new Operation(date, libelle, montant));
        return _comptes[compte.Id] = compte with { Solde = compte.Solde + montant };
    }
}
