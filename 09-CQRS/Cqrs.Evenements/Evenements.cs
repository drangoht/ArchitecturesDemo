using System.Text.Json;

namespace Cqrs.Evenements;

/// <summary>Un fait qui s'est produit dans le domaine (toujours au passé).</summary>
public interface IEvenementDomaine
{
    DateTime Date { get; }
}

public record CompteOuvert(Guid CompteId, string Titulaire, decimal DepotInitial, DateTime Date) : IEvenementDomaine;

public record ArgentRetire(Guid CompteId, decimal Montant, decimal NouveauSolde, DateTime Date) : IEvenementDomaine;

public record VirementEffectue(
    Guid VirementId, Guid SourceId, Guid CibleId, decimal Montant,
    decimal SoldeSource, decimal SoldeCible, DateTime Date) : IEvenementDomaine;

/// <summary>Un événement et sa position dans le flux (la séquence sert de point de reprise aux projections).</summary>
public record EvenementEnregistre(long Sequence, IEvenementDomaine Evenement);

/// <summary>Accès en lecture au flux d'événements du côté écriture.</summary>
public interface IFluxEvenements
{
    Task<IReadOnlyList<EvenementEnregistre>> LireApres(long sequence, int maximum);
    Task<long> DerniereSequence();
}

public static class CatalogueEvenements
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> Types =
        new[] { typeof(CompteOuvert), typeof(ArgentRetire), typeof(VirementEffectue) }.ToDictionary(t => t.Name);

    public static string Serialiser(IEvenementDomaine evenement) =>
        JsonSerializer.Serialize(evenement, evenement.GetType(), Json);

    public static IEvenementDomaine Deserialiser(string type, string json) =>
        (IEvenementDomaine)JsonSerializer.Deserialize(json, Types[type], Json)!;
}
