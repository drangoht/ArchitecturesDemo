using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Mt.Locataires;

namespace Mt.Banque;

/// <summary>
/// Choisit la base de chaque locataire : la base partagée (Standard) ou sa base dédiée (Premium).
/// C'est le seul endroit qui connaît cette différence ; le métier et le DbContext l'ignorent.
/// En production, ce serait une chaîne de connexion par locataire, lue dans le catalogue (ou dans un coffre de secrets).
/// </summary>
public sealed class FabriqueBanque(InMemoryDatabaseRoot racine)
{
    public const string BasePartagee = "banque-partagee";

    private readonly ConcurrentDictionary<string, byte> _bases = new();

    public static string NomBase(Locataire locataire) =>
        locataire.Isolation == Isolation.BaseDediee ? $"banque-{locataire.Id}" : BasePartagee;

    /// <summary>Les bases ouvertes jusqu'ici (pour l'inventaire de la plateforme).</summary>
    public IReadOnlyList<string> Bases => _bases.Keys.Order().ToList();

    /// <summary>La base du locataire courant (usage normal, dans une requête).</summary>
    public BanqueDbContext Ouvrir(IContexteLocataire contexte) => Ouvrir(NomBase(contexte.Exiger()), contexte);

    /// <summary>La base d'un locataire donné, pour la plateforme (inscription, migration, suppression).</summary>
    public BanqueDbContext Ouvrir(Locataire locataire) => Ouvrir(NomBase(locataire), ContexteLocataire.Pour(locataire));

    /// <summary>
    /// Une base vue par la plateforme, sans locataire : toute requête échoue, sauf avec <c>IgnoreQueryFilters()</c>.
    /// Voir tous les locataires doit être une décision explicite, visible dans le code.
    /// </summary>
    public BanqueDbContext OuvrirSansLocataire(string nomBase) => Ouvrir(nomBase, new ContexteLocataire());

    /// <summary>Supprime une base dédiée (départ d'un client, ou après sa migration).</summary>
    public async Task SupprimerBase(Locataire locataire)
    {
        await using var db = Ouvrir(locataire);
        await db.Database.EnsureDeletedAsync();
        _bases.TryRemove(NomBase(locataire), out _);
    }

    private BanqueDbContext Ouvrir(string nomBase, IContexteLocataire contexte)
    {
        _bases.TryAdd(nomBase, 0);
        var options = new DbContextOptionsBuilder<BanqueDbContext>().UseInMemoryDatabase(nomBase, racine).Options;
        return new BanqueDbContext(options, contexte);
    }
}
